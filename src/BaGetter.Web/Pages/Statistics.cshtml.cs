using BaGetter.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace BaGetter.Web;

public class StatisticsModel : PageModel
{
    private readonly IConfiguration _configuration;

    public StatisticsModel(IConfiguration configuration)
    {
        _configuration = configuration;

    }

    public IActionResult OnGet()
    {
        var options = _configuration.Get<BaGetterOptions>();

        if (options.Statistics.EnableStatisticsPage is false) return NotFound();

        return Page();
    }
}
