namespace RequestBridge.Abstractions;

/// <summary>
/// The only exception type a provider may throw across the abstraction boundary.
/// </summary>
/// <remarks>
/// <para>
/// A provider must not let its own transport or library exceptions escape. Doing
/// so would leak the provider's implementation into its caller, which is the
/// coupling this abstraction exists to prevent. Catch, classify, and rethrow as
/// this type.
/// </para>
/// <para>
/// Messages must not name the provider or quote its wording. The
/// <see cref="ErrorCode"/> is the contract; the message is a diagnostic aid.
/// </para>
/// </remarks>
public class ProviderException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderException"/> class.
    /// </summary>
    /// <param name="errorCode">The classification of the failure.</param>
    /// <param name="message">A provider-neutral description, for diagnostics.</param>
    public ProviderException(ProviderErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderException"/> class.
    /// </summary>
    /// <param name="errorCode">The classification of the failure.</param>
    /// <param name="message">A provider-neutral description, for diagnostics.</param>
    /// <param name="innerException">
    /// The underlying failure. Kept for logging, and never surfaced to a caller.
    /// </param>
    public ProviderException(ProviderErrorCode errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderException"/> class
    /// with an unclassified failure.
    /// </summary>
    public ProviderException()
        : base("The request provider failed.")
    {
        ErrorCode = ProviderErrorCode.Unknown;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderException"/> class
    /// with an unclassified failure.
    /// </summary>
    /// <param name="message">A provider-neutral description, for diagnostics.</param>
    public ProviderException(string message)
        : base(message)
    {
        ErrorCode = ProviderErrorCode.Unknown;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderException"/> class
    /// with an unclassified failure.
    /// </summary>
    /// <param name="message">A provider-neutral description, for diagnostics.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = ProviderErrorCode.Unknown;
    }

    /// <summary>
    /// Gets the classification of this failure.
    /// </summary>
    public ProviderErrorCode ErrorCode { get; }
}
