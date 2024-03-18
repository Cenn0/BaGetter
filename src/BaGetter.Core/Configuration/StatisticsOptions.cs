using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BaGetter.Core;

public class StatisticsOptions : IValidatableObject
{
    [Required]
    public bool? EnableStatisticsPage { get; set; } = false;

    [Required]
    public bool? ListConfiguredServices { get; set; } = false;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EnableStatisticsPage is null)
        {
            yield return new ValidationResult(
                $"The {nameof(EnableStatisticsPage)} needs to be true or false",
                new[] { nameof(EnableStatisticsPage) });
        }

        if (ListConfiguredServices is null)
        {
            yield return new ValidationResult(
                $"The {nameof(ListConfiguredServices)} needs to be true or false",
                new[] { nameof(ListConfiguredServices) });
        }
    }
}
