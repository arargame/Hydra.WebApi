using Hydra.Core;
using System.ComponentModel.DataAnnotations;

namespace Hydra.WebApi.Models
{
    public class Vessel : BaseObject<Vessel>
    {
        [MaxLength(4)]
        [MinLength(4)]
        public string? CallSign { get; set; } = null;

        [DataType(DataType.DateTime)]
        public DateTime BuildDate { get; set; }

        [MaxLength(320)]
        public string? EmailAddress { get; set; } = null;
    }
}
