namespace ExitInterviewAgent.Providers;

/// <summary>
/// An API key. It cannot be printed, logged, serialized or compared by accident: <see cref="ToString"/> is a fixed
/// placeholder, there is no implicit conversion, and the only reader is <see cref="Reveal"/>, which the transport adapters call once.
/// </summary>
public sealed class SecretString
{
    private readonly string _value;

    public SecretString(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ProviderConfigurationException("The API key is empty.");
        _value = value.Trim();
        if (_value.Any(char.IsControl) || _value.Any(char.IsWhiteSpace))
            throw new ProviderConfigurationException("The API key contains white space or control characters (a stray newline from a copy-paste?).");
    }

    public int Length => _value.Length;

    public override string ToString() => "[redacted]";

    internal string Reveal() => _value;
}
