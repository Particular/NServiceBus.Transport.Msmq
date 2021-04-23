using System;

class Timeout
{
    public DateTime Time { get; set; }
    public string Id { get; set; }
    public byte[] State { get; set; }
    public byte[] Headers { get; set; }
    public string Destination { get; set; }
}