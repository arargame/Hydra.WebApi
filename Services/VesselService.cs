using Hydra.WebApi.Models;

namespace Hydra.WebApi.Services
{
    public interface IVesselService
    {
        Task<Vessel?> GetByIdAsync(Guid id);
    }

    public class VesselService : IVesselService
    {
        // Örnek sabit veri, EF ile entegre edeceksen buraya DbContext gelir.
        private static readonly List<Vessel> DummyData = new()
        {
            new Vessel { Id = new Guid("BBCE562A-848B-4FAC-83ED-189216750063"), Name = "Hydra I", },
            new Vessel { Id = Guid.NewGuid(), Name = "Hydra II", },
        };

        public Task<Vessel?> GetByIdAsync(Guid id)
        {
            var vessel = DummyData.FirstOrDefault(x => x.Id == id);
            return Task.FromResult(vessel);
        }
    }


}
