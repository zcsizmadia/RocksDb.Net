namespace RocksDbNet;

/// <summary>
/// Turns a value read from the database into a <typeparamref name="T"/>,
/// reading it in place.
/// </summary>
/// <typeparam name="T">What the value decodes to.</typeparam>
/// <param name="value">
/// The value, pointing into RocksDb's own memory. Valid only until the
/// decoder returns: do not keep it, or anything that refers to it.
/// </param>
/// <returns>The decoded value.</returns>
/// <remarks>
/// <para>
/// Used by <see cref="RocksDb.TryGet{T}(ReadOnlySpan{byte}, ValueDecoder{T}, out T, ReadOptions?)"/>
/// and its counterparts, which read through a pinned slice so the value is
/// never copied into a managed array. A <c>static</c> lambda is cached by the
/// compiler, so passing one allocates nothing:
/// <code>
/// db.TryGet(key, static v => BinaryPrimitives.ReadInt64LittleEndian(v), out long counter);
/// </code>
/// </para>
/// <para>
/// A delegate rather than <c>Func&lt;ReadOnlySpan&lt;byte&gt;, T&gt;</c>,
/// because a span cannot be a generic argument on every framework this library
/// targets.
/// </para>
/// </remarks>
public delegate T ValueDecoder<T>(ReadOnlySpan<byte> value);

/// <summary>
/// Turns a value read from the database into a <typeparamref name="T"/>,
/// reading it in place, with state passed in rather than captured.
/// </summary>
/// <typeparam name="TState">The state the decoder needs, such as serializer options.</typeparam>
/// <typeparam name="T">What the value decodes to.</typeparam>
/// <param name="value">
/// The value, pointing into RocksDb's own memory. Valid only until the
/// decoder returns.
/// </param>
/// <param name="state">The state passed alongside the decoder.</param>
/// <returns>The decoded value.</returns>
/// <remarks>
/// The form to use when the decoder needs something from its surroundings. A
/// lambda that captures it allocates a closure on every call; one that takes
/// it as <paramref name="state"/> can be <c>static</c> and allocates nothing.
/// The same pattern as <see cref="string.Create{TState}(int, TState, System.Buffers.SpanAction{char, TState})"/>.
/// </remarks>
public delegate T ValueDecoder<in TState, T>(ReadOnlySpan<byte> value, TState state);
