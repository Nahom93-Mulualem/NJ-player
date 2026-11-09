using System;

namespace NJPlayer.Features.Trimmer;

/// <summary>
/// Options for trimming a media file.
/// </summary>
public sealed class TrimOptions
{
    public string InputFilePath { get; set; } = string.Empty;
    public string OutputFilePath { get; set; } = string.Empty;
    public TimeSpan InPoint { get; set; } = TimeSpan.Zero;
    public TimeSpan OutPoint { get; set; } = TimeSpan.Zero;
    public bool ReEncode { get; set; } = false;

    public TimeSpan Duration => OutPoint > InPoint ? OutPoint - InPoint : TimeSpan.Zero;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(InputFilePath))
            throw new ArgumentException("Input file path must be specified.", nameof(InputFilePath));

        if (string.IsNullOrWhiteSpace(OutputFilePath))
            throw new ArgumentException("Output file path must be specified.", nameof(OutputFilePath));

        if (OutPoint <= InPoint)
            throw new ArgumentException("Out-point must be greater than in-point.", nameof(OutPoint));
    }
}

