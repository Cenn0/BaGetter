using BaGetter.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BaGetter.Web;

public class StatisticsModel : PageModel
{
    private readonly IOptionsSnapshot<BaGetterOptions> _options;

    public StatisticsModel(IOptionsSnapshot<BaGetterOptions> options)
    {
        _options = options;
    }

    public IActionResult OnGet()
    {
        if (!_options.Value.Statistics.EnableStatisticsPage) return NotFound();

        return Page();
    }
}
