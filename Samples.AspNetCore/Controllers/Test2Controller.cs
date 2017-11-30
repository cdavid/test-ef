using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Samples.AspNetCore.Models;
using StackExchange.Profiling;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Samples.AspNetCore.Controllers
{
    /// <summary>
    /// Controller with Context coming from DI
    /// </summary>
    public class Test2Controller : Controller
    {
        private readonly SampleContext _context;
        private readonly MiniProfilerOptions _miniProfilerOptions;

        public static Guid LastUpdate;

        public Test2Controller(IOptions<MiniProfilerOptions> options, SampleContext context)
        {
            _miniProfilerOptions = options.Value;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetHitListAsync()
        {
            if (!ModelState.IsValid) return BadRequest();

            LastUpdate = MiniProfiler.Current.Id;

            using (MiniProfiler.Current.Step("EF Core Stuff"))
            {
                using (MiniProfiler.Current.Step("Get items"))
                {
                    return Ok(await _context.RouteHits.Select(x => x.Id).ToListAsync().ConfigureAwait(false));
                }
            }
        }

        [HttpGet]
        public async Task<IActionResult> UpdateItemAsync(Guid id)
        {
            RouteHit hit;
            using (MiniProfiler.Current.Step("EF Core Stuff"))
            {
                using (MiniProfiler.Current.Step("Get Existing"))
                {
                    hit = await _context.RouteHits.FirstOrDefaultAsync(h => h.Id == id).ConfigureAwait(false);
                }

                if (hit != null)
                {
                    using (MiniProfiler.Current.Step("Update"))
                    {
                        hit.UpdateTime = DateTime.UtcNow;
                        await _context.SaveChangesAsync().ConfigureAwait(false);
                    }
                }
            }

            return Ok(id);
        }

        [HttpGet]
        public async Task<IActionResult> GetProfilingDataSinceLastPopulateAsync()
        {
            var guids = await _miniProfilerOptions.Storage.ListAsync(10000).ConfigureAwait(false);
            guids = guids.TakeWhile(g => g != LastUpdate);

            var items = guids.Reverse().Select(g => _miniProfilerOptions.Storage.Load(g)).Where(p => p != null);
            var output = "Id,Started,TotalMs,"
                + "GetStart,GetMs,"
                + "GetOpenStart,GetOpenMs,GetSelectStart,GetSelectMs,GetCloseStart,GetCloseMs,"
                + "UpdateStart,UpdateMs,"
                + "UpdateOpenStart,UpdateOpenMs,UpdateSelectStart,UpdateSelectMs,UpdateCloseStart,UpdateCloseMs"
                + Environment.NewLine;

            foreach (var item in items)
            {
                var get = item.Root.Children[1].Children[0].Children[0];
                var getSql = get.CustomTimings["sql"];

                var update = item.Root.Children[1].Children[0].Children[1];
                var updateSql = update.CustomTimings["sql"];

                output += $"{item.Id},{item.Started.ToString("yyyy-MM-ddThh:mm:ss.ffffff")},{item.DurationMilliseconds},"
                    + $"{get.StartMilliseconds},{get.DurationMilliseconds}," // Get items
                    + $"{getSql[0].StartMilliseconds},{getSql[0].DurationMilliseconds},{getSql[1].StartMilliseconds},{getSql[1].DurationMilliseconds},{getSql[2].StartMilliseconds},{getSql[2].DurationMilliseconds}," // Get SQL
                    + $"{update.StartMilliseconds},{update.DurationMilliseconds}," // Update items
                    + $"{updateSql[0].StartMilliseconds},{updateSql[0].DurationMilliseconds},{updateSql[1].StartMilliseconds},{updateSql[1].DurationMilliseconds},{updateSql[2].StartMilliseconds},{updateSql[2].DurationMilliseconds}" // Update SQL
                    + Environment.NewLine;
            }

            return Ok(output);
        }
    }
}
