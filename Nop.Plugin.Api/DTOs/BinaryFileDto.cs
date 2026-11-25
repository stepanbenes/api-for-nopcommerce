#nullable enable

namespace Nop.Plugin.Api.DTOs
{
    public class BinaryFileDto
    {
        public string FileName { get; init; } = null!;
        public string MimeType { get; init; } = null!;
        public byte[] Content { get; init; } = null!;
        public DateTimeOffset CreatedAt { get; init; }
    }
}
