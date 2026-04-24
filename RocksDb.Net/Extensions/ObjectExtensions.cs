using System.Reflection;

namespace RocksDbNet.Extensions;

public static class ObjectExtensions
{
    public static bool CheckIfMethodOverridden<T>(this object instance, string methodName)
    {
        return instance.CheckIfMethodOverridden(typeof(T), methodName);
    }

    public static bool CheckIfMethodOverridden(this object instance, Type baseType, string methodName)
    {
        if (instance == null)
        {
            return false;
        }

        var method = instance
            .GetType()
            .GetMethod(methodName,BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (method == null)
        {
            return false;
        }

        // Check if method is virtual or abstract
        if (!method.IsVirtual && !method.IsAbstract)
        {
            return false;
        }

        // Check if the declaring type is different from the provided base type
        return method.DeclaringType != baseType;
    }
}