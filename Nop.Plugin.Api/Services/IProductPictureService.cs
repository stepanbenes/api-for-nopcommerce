using Nop.Core.Domain.Catalog;

namespace Nop.Plugin.Api.Services
{
    public interface IProductPictureService
    {
        Task<ProductPicture> GetProductPictureByPictureIdAsync(int pictureId);
    }
}
