using System;

namespace EduScroll.Application.Interfaces.Pdf;

public interface IPdfTextExtractor
{
    public Task<IReadOnlyList<string>> ExtractAsync(Stream PdfStream, CancellationToken cancellationToken = default);

}
