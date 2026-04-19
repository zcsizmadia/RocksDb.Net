using System.Text.RegularExpressions;

namespace NativeMethodsGenerator;

/// <summary>
/// Represents a single C function parameter.
/// </summary>
public record CParam(string Type, string Name);

/// <summary>
/// Represents a parsed C function declaration.
/// </summary>
public record CFunction(string ReturnType, string Name, List<CParam> Parameters);

/// <summary>
/// Parses exported function declarations from rocksdb's c.h header.
/// </summary>
public static partial class CHeaderParser
{
    /// <summary>
    /// Extracts all ROCKSDB_LIBRARY_API function declarations from the header text.
    /// </summary>
    public static List<CFunction> Parse(string headerText)
    {
        var functions = new List<CFunction>();

        // Remove C/C++ comments
        var text = StripComments(headerText);

        // Match ROCKSDB_LIBRARY_API declarations.
        // We match the prefix up to the opening paren, then extract the balanced
        // parameter list manually to handle nested parentheses in function pointers.
        var prefixPattern = ExportedFunctionPrefixRegex();

        foreach (Match m in prefixPattern.Matches(text))
        {
            var returnType = NormalizeWhitespace(m.Groups["ret"].Value);
            var name = m.Groups["name"].Value.Trim();

            // Find the balanced parameter list starting after the '(' matched by the regex
            int openParen = m.Index + m.Length - 1; // position of '('
            var paramStr = ExtractBalancedParens(text, openParen);
            if (paramStr == null)
                continue;

            // Expect a semicolon after the closing paren
            int afterClose = openParen + paramStr.Length + 2; // +2 for ( and )
            var trailing = text[afterClose..].TrimStart();
            if (trailing.Length == 0 || trailing[0] != ';')
                continue;

            var parameters = ParseParameters(paramStr);
            functions.Add(new CFunction(returnType, name, parameters));
        }

        return functions;
    }

    /// <summary>
    /// Extracts the content between balanced parentheses starting at the given '(' position.
    /// Returns the inner content (excluding outer parens), or null if unbalanced.
    /// </summary>
    private static string? ExtractBalancedParens(string text, int openIndex)
    {
        if (openIndex >= text.Length || text[openIndex] != '(')
            return null;

        int depth = 1;
        int i = openIndex + 1;
        while (i < text.Length && depth > 0)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') depth--;
            i++;
        }

        if (depth != 0)
            return null;

        // Return content between outer parens (exclusive)
        return text[(openIndex + 1)..(i - 1)];
    }

    private static List<CParam> ParseParameters(string paramStr)
    {
        paramStr = paramStr.Trim();
        if (string.IsNullOrWhiteSpace(paramStr) || paramStr == "void")
            return [];

        var parameters = new List<CParam>();
        // Split by comma, but be careful of function pointer types with commas inside parens
        var parts = SplitParameters(paramStr);

        foreach (var part in parts)
        {
            var trimmed = NormalizeWhitespace(part.Trim());
            if (string.IsNullOrEmpty(trimmed) || trimmed == "...")
            {
                // Varargs - skip or add special marker
                if (trimmed == "...")
                    parameters.Add(new CParam("...", "varargs"));
                continue;
            }

            // Parse type and name from the parameter
            var (type, name) = SplitTypeAndName(trimmed);
            parameters.Add(new CParam(type, name));
        }

        return parameters;
    }

    private static (string Type, string Name) SplitTypeAndName(string param)
    {
        // Handle function pointer parameters: type (*name)(args)
        var fpMatch = FuncPtrParamRegex().Match(param);
        if (fpMatch.Success)
        {
            return (param, fpMatch.Groups["fpname"].Value);
        }

        // Handle "const char* const* name" patterns
        // Handle pointer types: work backwards from end to find the name
        param = param.Trim();

        // Handle C array syntax: "type name[]" → treat as pointer type
        if (param.EndsWith("[]"))
        {
            param = param[..^2].Trim(); // Remove []
            // Now split type and name, then add * to the type
            var (innerType, innerName) = SplitTypeAndName(param);
            return (innerType + "*", innerName);
        }

        // Try to find the last identifier token that isn't a keyword/qualifier
        // Pattern: everything up to last whitespace-separated token is the type, last token is the name
        // But pointers complicate this: "char**" vs "char** name"

        // If ends with *, it's a pure type with no name (unnamed param)
        if (param.EndsWith('*'))
            return (param, "");

        // Find last space or * that separates type from name
        int lastSep = -1;
        for (int i = param.Length - 1; i >= 0; i--)
        {
            if (param[i] == ' ' || param[i] == '*')
            {
                lastSep = i;
                break;
            }
        }

        if (lastSep < 0)
        {
            // Single token - it's a type (like "void")
            return (param, "");
        }

        var type = param[..(lastSep + 1)].Trim();
        var name = param[(lastSep + 1)..].Trim();

        // If name looks like a type keyword, treat whole thing as type
        if (IsTypeKeyword(name))
            return (param, "");

        return (type, name);
    }

    private static bool IsTypeKeyword(string s) =>
        s is "void" or "int" or "char" or "unsigned" or "signed" or "long"
           or "short" or "double" or "float" or "size_t" or "uint64_t"
           or "uint32_t" or "int32_t" or "int64_t";

    private static List<string> SplitParameters(string paramStr)
    {
        var result = new List<string>();
        int depth = 0;
        int start = 0;

        for (int i = 0; i < paramStr.Length; i++)
        {
            switch (paramStr[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    result.Add(paramStr[start..i]);
                    start = i + 1;
                    break;
            }
        }

        if (start < paramStr.Length)
            result.Add(paramStr[start..]);

        return result;
    }

    private static string StripComments(string text)
    {
        // Remove block comments /* ... */
        text = BlockCommentRegex().Replace(text, " ");
        // Remove line comments // ...
        text = LineCommentRegex().Replace(text, "");
        return text;
    }

    private static string NormalizeWhitespace(string s) =>
        WhitespaceRegex().Replace(s.Trim(), " ");

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineCommentRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(
        @"extern\s+ROCKSDB_LIBRARY_API\s+(?<ret>[\w\s\*]+?)\s+(?<name>rocksdb_\w+)\s*\(",
        RegexOptions.Singleline)]
    private static partial Regex ExportedFunctionPrefixRegex();

    [GeneratedRegex(@"\(\s*\*\s*(?<fpname>\w*)\s*\)")]
    private static partial Regex FuncPtrParamRegex();
}
