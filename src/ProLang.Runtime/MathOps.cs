namespace ProLang.Runtime;

/// <summary>
/// Numeric operations for ProLang's <c>math</c> module.
/// </summary>
public static class MathOps
{
    /// <summary>
    /// Returns a non-negative random integer below <paramref name="maxValue"/>.
    /// </summary>
    /// <remarks>
    /// Wraps <see cref="Random.Shared"/>. Emitting this as IL needed a scratch local at every
    /// call site, because <c>maxValue</c> is pushed before the receiver and the two have to be
    /// swapped.
    /// </remarks>
    public static int Random(int maxValue) => System.Random.Shared.Next(maxValue);
}
