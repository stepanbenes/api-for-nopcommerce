using Nop.Core.Infrastructure;
using Nop.Plugin.Api.DTO.Images;
using Nop.Services.Media;
using System.Text.RegularExpressions;


namespace Nop.Plugin.Api.Attributes
{
    public class ImageValidationAttribute : BaseValidationAttribute
    {
        private readonly Dictionary<string, string> _errors;
        private readonly IPictureService _pictureService;

        public ImageValidationAttribute()
        {
            _errors = new Dictionary<string, string>();
            _pictureService = EngineContext.Current.Resolve<IPictureService>();
        }

        public override async Task ValidateAsync(object instance)
        {
            var imageDto = instance as ImageDto;

            var imageSrcSet = imageDto != null && !string.IsNullOrEmpty(imageDto.Src);
            var imageAttachmentSet = imageDto != null && !string.IsNullOrEmpty(imageDto.Attachment);


            if (imageSrcSet || imageAttachmentSet)
            {
                byte[] imageBytes = null;
                var mimeType = string.Empty;

                // Validation of the image object

                // We can't have both set.
                CheckIfBothImageSourceTypesAreSet(imageSrcSet, imageAttachmentSet);

                // Here we ensure that the validation to this point has passed 
                // and try to download the image or convert base64 format to byte array
                // depending on which format is passed. In both cases we should get a byte array and mime type.
                if (_errors.Count == 0)
                {
                    if (imageSrcSet)
                    {
                        DownloadFromSrc(imageDto.Src, ref imageBytes, ref mimeType);
                    }
                    else if (imageAttachmentSet)
                    {
                        ValidateAttachmentFormat(imageDto.Attachment);

                        if (_errors.Count == 0)
                        {
                            ConvertAttachmentToByteArray(imageDto.Attachment, ref imageBytes,
                                                         ref mimeType);
                        }
                    }
                }

                // We need to check because some of the validation above may have render the models state invalid.
                if (_errors.Count == 0)
                {
                    // Here we handle the check if the file passed is actual image and if the image is valid according to the 
                    // restrictions set in the administration.
                    await ValidatePictureByteArrayAsync(imageBytes, mimeType);
                }

                imageDto.Binary = imageBytes;
                imageDto.MimeType = mimeType;
            }
        }

        public override Dictionary<string, string> GetErrors()
        {
            return _errors;
        }

        private void CheckIfBothImageSourceTypesAreSet(bool imageSrcSet, bool imageAttachmentSet)
        {
            if (imageSrcSet &&
                imageAttachmentSet)
            {
                const string Key = "image type";
                _errors.Add(Key, "Image src and Attachment are both set");
            }
        }

        private void DownloadFromSrc(string imageSrc, ref byte[] imageBytes, ref string mimeType)
        {
            const string Key = "image type";

            var client = new HttpClient();

            try
            {
                //webClient version
                var response = client.GetAsync(imageSrc).Result;
                imageBytes = response.Content.ReadAsByteArrayAsync().Result;
                if (response.Content.Headers.ContentType != null)
                {
                    mimeType = response.Content.Headers.ContentType.MediaType;
                }

                if (imageBytes == null)
                {
                    _errors.Add(Key, "src is invalid");
                }
            }
            catch (Exception ex)
            {
                var message = $"src is invalid - {ex.Message}";

                _errors.Add(Key, message);
            }
        }

        private void ValidateAttachmentFormat(string attachment)
        {
            var validBase64Pattern =
                new Regex("^([A-Za-z0-9+/]{4})*([A-Za-z0-9+/]{4}|[A-Za-z0-9+/]{3}=|[A-Za-z0-9+/]{2}==)$");
            var isMatch = validBase64Pattern.IsMatch(attachment);
            if (!isMatch)
            {
                const string Key = "image type";
                _errors.Add(Key, "attachment format is invalid");
            }
        }

        private static void ConvertAttachmentToByteArray(string attachment, ref byte[] imageBytes, ref string mimeType)
        {
            imageBytes = Convert.FromBase64String(attachment);
            mimeType = MimeTypes.GetMimeType(attachment);
        }



        private async Task ValidatePictureByteArrayAsync(byte[] imageBytes, string mimeType)
        {
            if (imageBytes != null)
            {
                try
                {
                    imageBytes = await _pictureService.ValidatePictureAsync(imageBytes, mimeType, "placeholderToFix");
                }
                catch (Exception ex)
                {
                    var key = "image invalid";
                    var message = $"source is invalid - {ex.Message}";

                    _errors.Add(key, message);
                }
            }

            if (imageBytes == null)
            {
                var key = "image invalid";
                var message = "You have provided an invalid image source/attachment";

                _errors.Add(key, message);
            }
        }
    }
}
