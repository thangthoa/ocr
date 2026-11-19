namespace Modules.Ocr.Infrastructure.Pdf;

/// <summary>An image ready to be written as a PDF image XObject.</summary>
/// <param name="Width">Width in pixels, as stored.</param>
/// <param name="Height">Height in pixels, as stored.</param>
/// <param name="Entries">Image dictionary entries other than Type, Subtype, Width, Height and Length.</param>
/// <param name="Data">The stream data, already encoded with the filter named in <paramref name="Entries"/>.</param>
/// <param name="Dpi">Resolution recorded in the file, if any.</param>
/// <param name="Orientation">EXIF orientation, 1–8; 1 shows the image as stored.</param>
internal sealed record PdfImage(int Width, int Height, string Entries, ReadOnlyMemory<byte> Data, double? Dpi, int Orientation = 1);
