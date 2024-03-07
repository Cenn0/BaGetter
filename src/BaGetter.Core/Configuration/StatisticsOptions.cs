using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BaGetter.Core;

public class StatisticsOptions : IValidatableObject
{
    [Required]
    public bool? HideServices { get; set; } = false;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (HideServices is null)
        {
            yield return new ValidationResult(
                $"The {nameof(HideServices)} needs to be true or false",
                new[] { nameof(HideServices) });
        }
    }
}
