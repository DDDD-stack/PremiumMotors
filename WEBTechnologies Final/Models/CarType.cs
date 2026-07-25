using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models
{

    public enum CarType
    {
        Sedan,
        SUV,
        Hatchback,
        Coupe,
        Convertible,
        Wagon,
        Pickup,
        Van,

        [Display(Name = "Sports Car")]
        SportsCar,

        Other
    }
}
