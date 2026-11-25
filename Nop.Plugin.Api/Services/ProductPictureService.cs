using Nop.Core.Domain.Catalog;
using Nop.Data;

namespace Nop.Plugin.Api.Services
{
    public class ProductPictureService : IProductPictureService
    {
        private readonly IRepository<ProductPicture> _productPictureRepository;

        public ProductPictureService(IRepository<ProductPicture> productPictureRepository)
        {
            _productPictureRepository = productPictureRepository;
        }

        public async Task<ProductPicture> GetProductPictureByPictureIdAsync(int pictureId)
        {
            if (pictureId == 0)
            {
                return null;
            }

            var query = from pp in _productPictureRepository.Table
                        where pp.PictureId == pictureId
                        select pp;

            return await query.FirstOrDefaultAsync();
        }
    }
}
