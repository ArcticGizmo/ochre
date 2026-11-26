namespace Ochre.Core.Bridge;

/// <summary>A message the UI may send, with its request and response records (ADR 0009).</summary>
public sealed record RequestContract(string Type, Type Request, Type Response);

/// <summary>An event the host may push to every window, with its payload record.</summary>
public sealed record EventContract(string Name, Type Payload);

public sealed record Empty;

public sealed record Ack(bool Ok = true);
