using System;

/// <summary>
///
/// </summary>
public class TimeoutItem
{
    /// <summary>
    ///
    /// </summary>
    public DateTime Time { get; set; }
    /// <summary>
    ///
    /// </summary>
    public string Id { get; set; }
    /// <summary>
    ///
    /// </summary>
    public byte[] State { get; set; }
    /// <summary>
    ///
    /// </summary>
    public byte[] Headers { get; set; }
    /// <summary>
    ///
    /// </summary>
    public string Destination { get; set; }
    /// <summary>
    ///
    /// </summary>
    public int NrOfRetries { get; set; }
}