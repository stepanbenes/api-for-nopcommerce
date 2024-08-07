namespace Nop.Plugin.Api.DTOs
{
    public class BinaryFileDto
    {
        public string FileName { get; init; }
        public string MimeType { get; init; }
        public byte[] Content { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }
}
