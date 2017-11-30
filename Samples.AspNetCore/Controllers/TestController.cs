using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Samples.AspNetCore.Models;
using StackExchange.Profiling;
using StackExchange.Profiling.Data;
using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Samples.AspNetCore.Controllers
{
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
                    hit = await context.RouteHits.FirstOrDefaultAsync(h => h.Id == id).ConfigureAwait(false);
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

    public class TestController : Controller
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly MiniProfilerOptions _miniProfilerOptions;
        private readonly ILogger _logger;

        public static Guid LastUpdate;

        public TestController(IServiceProvider serviceProvider, IOptions<MiniProfilerOptions> options, ILoggerFactory loggerFactory)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _miniProfilerOptions = options.Value;
            _logger = loggerFactory.CreateLogger<TestController>();
        }

        [HttpGet]
        public IActionResult Ping()
        {
            return Ok("pong");
        }

        [HttpGet]
        public async Task<IActionResult> PopulateDatabaseAsync([FromQuery] int count)
        {
            if (!ModelState.IsValid) return BadRequest();

            SampleContext context = null;
            int existingCount = 0;
            using (MiniProfiler.Current.Step("EF Core Stuff"))
            {
                using (MiniProfiler.Current.Step("Create Context"))
                {
                    context = (SampleContext)_serviceProvider.GetService(typeof(SampleContext));
                }

                using (MiniProfiler.Current.Step("Get Existing"))
                {
                    existingCount = context.RouteHits.Count();
                }

                if (existingCount < count)
                {
                    using (MiniProfiler.Current.Step("Insert"))
                    {
                        for (int i = existingCount; i < count; i++)
                        {
                            context.Add(new RouteHit()
                            {
                                Id = Guid.NewGuid(),
                                Name = "name" + i.ToString(),
                                UpdateTime = DateTime.UtcNow
                            });
                        }

                        await context.SaveChangesAsync().ConfigureAwait(false);
                    }
                }

                return Ok(count);
            }
        }

        [HttpGet]
        public async Task<IActionResult> CleanDatabaseAsync()
        {
            if(!ModelState.IsValid) return BadRequest();

            SampleContext context = null;
            using (MiniProfiler.Current.Step("EF Core Stuff"))
            {
                using (MiniProfiler.Current.Step("Create Context"))
                {
                    context = (SampleContext)_serviceProvider.GetService(typeof(SampleContext));
                }

                using (MiniProfiler.Current.Step("Delete Existing"))
                {
                    context.RemoveRange(await context.RouteHits.ToListAsync().ConfigureAwait(false));
                    await context.SaveChangesAsync().ConfigureAwait(false);
                }

                return Ok();
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetHitListAsync()
        {
            if (!ModelState.IsValid) return BadRequest();

            LastUpdate = MiniProfiler.Current.Id;

            SampleContext context = null;
            using (MiniProfiler.Current.Step("EF Core Stuff"))
            {
                using (MiniProfiler.Current.Step("Create Context"))
                {
                    context = (SampleContext)_serviceProvider.GetService(typeof(SampleContext));
                    context.Database.EnsureCreated();
                }

                using (MiniProfiler.Current.Step("Get items"))
                {
                    return Ok(await context.RouteHits.Select(x => x.Id).ToListAsync().ConfigureAwait(false));
                }
            }
        }

        [HttpGet]
        public async Task<IActionResult> UpdateItemAsync(Guid id)
        {
            RouteHit hit;
            SampleContext context = null;
            using (MiniProfiler.Current.Step("EF Core Stuff"))
            {
                try
                {
                    using (MiniProfiler.Current.Step("Create Context"))
                    {
                        context = (SampleContext)_serviceProvider.GetService(typeof(SampleContext));
                    }

                    using (MiniProfiler.Current.Step("Get Existing"))
                    {
                        hit = await context.RouteHits.FirstOrDefaultAsync(h => h.Id == id).ConfigureAwait(false);
                    }

                    if (hit != null)
                    {
                        using (MiniProfiler.Current.Step("Update"))
                        {
                            hit.UpdateTime = DateTime.UtcNow;
                            await context.SaveChangesAsync().ConfigureAwait(false);
                        }
                    }
                }
                finally
                {
                    context?.Dispose();
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
                + "ContextStart,ContextMs,"
                + "GetStart,GetMs,"
                + "GetOpenStart,GetOpenMs,GetSelectStart,GetSelectMs,GetCloseStart,GetCloseMs,"
                + "UpdateStart,UpdateMs,"
                + "UpdateOpenStart,UpdateOpenMs,UpdateSelectStart,UpdateSelectMs,UpdateCloseStart,UpdateCloseMs"
                + Environment.NewLine;

            foreach (var item in items)
            {
                var createContext = item.Root.Children[1].Children[0].Children[0];

                var get = item.Root.Children[1].Children[0].Children[1];
                var getSql = get.CustomTimings["sql"];

                var update = item.Root.Children[1].Children[0].Children[2];
                var updateSql = update.CustomTimings["sql"];

                output += $"{item.Id},{item.Started.ToString("yyyy-MM-ddThh:mm:ss.ffffff")},{item.DurationMilliseconds},"
                    + $"{createContext.StartMilliseconds},{createContext.DurationMilliseconds}," // Create context
                    + $"{get.StartMilliseconds},{get.DurationMilliseconds}," // Get items
                    + $"{getSql[0].StartMilliseconds},{getSql[0].DurationMilliseconds},{getSql[1].StartMilliseconds},{getSql[1].DurationMilliseconds},{getSql[2].StartMilliseconds},{getSql[2].DurationMilliseconds}," // Get SQL
                    + $"{update.StartMilliseconds},{update.DurationMilliseconds}," // Update items
                    + $"{updateSql[0].StartMilliseconds},{updateSql[0].DurationMilliseconds},{updateSql[1].StartMilliseconds},{updateSql[1].DurationMilliseconds},{updateSql[2].StartMilliseconds},{updateSql[2].DurationMilliseconds}" // Update SQL
                    + Environment.NewLine;
            }

            return Ok(output);
        }

        #region not used

        public IActionResult EntityFrameworkCore()
        {
            DateTime now;
            RouteHit hit;
            SampleContext context = null;
            using (MiniProfiler.Current.Step("EF Core Stuff"))
            {
                const string name = "Test/EntityFrameworkCore";
                try
                {
                    using (MiniProfiler.Current.Step("Create Context"))
                    {
                        context = (SampleContext)_serviceProvider.GetService(typeof(SampleContext));
                    }

                    using (MiniProfiler.Current.Step("Get Existing"))
                    {
                        hit = context.RouteHits.FirstOrDefault(h => h.Name == name);
                    }

                    if (hit == null)
                    {
                        using (MiniProfiler.Current.Step("Insertion"))
                        {
                            context.RouteHits.Add(hit = new RouteHit { Name = name, UpdateTime = DateTime.UtcNow });
                            context.SaveChanges();
                        }
                    }
                    else
                    {
                        using (MiniProfiler.Current.Step("Update"))
                        {
                            hit.UpdateTime = DateTime.UtcNow;
                            context.SaveChanges();
                        }
                    }
                    now = hit.UpdateTime.Value;
                }
                finally
                {
                    context?.Dispose();
                }
            }

            return Content("EF complete - now: " + now);
        }

        public ActionResult EnableProfilingUI()
        {
            Program.DisableProfilingResults = false;
            return Redirect("/");
        }

        /// <summary>
        /// disable the profiling UI.
        /// </summary>
        /// <returns>disable profiling the UI</returns>
        public ActionResult DisableProfilingUI()
        {
            Program.DisableProfilingResults = true;
            return Redirect("/");
        }

        public IActionResult DuplicatedQueries()
        {
            using (var conn = GetConnection())
            {
                long total = 0;

                for (int i = 0; i < 20; i++)
                {
                    total += conn.QueryFirst<long>("select count(1) from RouteHits where HitCount = @i", new { i });
                }
                return Content(string.Format("Duplicated Queries (N+1) completed {0}", total));
            }
        }

        public async Task<IActionResult> DuplicatedQueriesAsync()
        {
            using (var conn = await GetConnectionAsync().ConfigureAwait(false))
            {
                long total = 0;

                for (int i = 0; i < 20; i++)
                {
                    total += await conn.QueryFirstAsync<long>("select count(1) from RouteHits where HitCount = @i", new { i }).ConfigureAwait(false);
                }
                return Content(string.Format("Duplicated Queries (N+1) completed {0}", total));
            }
        }

        public IActionResult MassiveViewNesting() => View("Tree");

        public IActionResult MassiveNesting()
        {
            var i = 0;
            using (var conn = GetConnection())
            {
                RecursiveMethod(ref i, conn, MiniProfiler.Current);
            }
            return Content("Massive Nesting completed");
        }

        public IActionResult MassiveNesting2()
        {
            for (int i = 0; i < 6; i++)
            {
                MassiveNesting();
            }
            return Content("Massive Nesting 2 completed");
        }

        private void RecursiveMethod(ref int depth, DbConnection connection, MiniProfiler profiler)
        {
            Thread.Sleep(5); // ensure we show up in the profiler

            if (depth >= 10) return;

            using (profiler.Step("Nested call " + depth))
            {
                // run some meaningless queries to illustrate formatting
                connection.Query(@"
Select *
  From MiniProfilers
 Where DurationMilliseconds >= @duration
    Or Started > @yesterday",
                    new
                    {
                        name = "Home/Index",
                        duration = 100.5,
                        yesterday = DateTime.UtcNow.AddDays(-1)
                    });

                connection.Query("Select RouteName, HitCount From RouteHits Where HitCount < 100000000 Or HitCount > 0 Order By HitCount, RouteName -- this should hopefully wrap");

                // massive query to test if max-height is properly removed from <pre> stylings
                connection.Query(@"
Select *
  From (Select RouteName, HitCount
          From RouteHits
         Where HitCount Between 0 and 9
        UNION ALL
        Select RouteName, HitCount
          From RouteHits
         Where HitCount Between 10 and 19
        UNION ALL
        Select RouteName, HitCount
          From RouteHits
         Where HitCount Between 20 and 29
        UNION ALL
        Select RouteName, HitCount
          From RouteHits
         Where HitCount Between 30 and 39
        UNION ALL
        Select RouteName, HitCount
          From RouteHits
         Where HitCount Between 40 and 49
        UNION ALL
        Select RouteName, HitCount
          From RouteHits
         Where HitCount Between 50 and 59
        UNION ALL
        Select RouteName, HitCount
          From RouteHits
         Where HitCount Between 60 and 69
        UNION ALL
        Select RouteName, HitCount
          From RouteHits
         Where HitCount Between 70 and 79
        UNION ALL
        Select RouteName, HitCount
          From RouteHits
         Where HitCount Between 80 and 89
        UNION ALL
        Select RouteName, HitCount
          From RouteHits
         Where HitCount Between 90 and 99
        UNION ALL
        Select RouteName, HitCount
          From RouteHits
         Where HitCount > 100)
Order By RouteName");

                // need a long title to test max-width
                using (profiler.Step("Incrementing a reference parameter named i"))
                {
                    depth++;
                }
                RecursiveMethod(ref depth, connection, profiler);
            }
        }

        public IActionResult MinSaveMs()
        {
            var profiler = MiniProfiler.Current;

            using (profiler.StepIf("Should show up", 50))
            {
                Thread.Sleep(60);
            }
            using (profiler.StepIf("Should not show up", 50))
            {
                Thread.Sleep(10);
            }

            using (profiler.StepIf("Show show up with children", 10, true))
            {
                Thread.Sleep(5);
                using (profiler.Step("Step A"))
                {
                    Thread.Sleep(10);
                }
                using (profiler.Step("Step B"))
                {
                    Thread.Sleep(10);
                }
                using (profiler.StepIf("Should not show up", 15))
                {
                    Thread.Sleep(10);
                }
            }

            using (profiler.StepIf("Show Not show up with children", 10))
            {
                Thread.Sleep(5);
                using (profiler.Step("Step A"))
                {
                    Thread.Sleep(10);
                }
                using (profiler.Step("Step B"))
                {
                    Thread.Sleep(10);
                }
            }

            using (profiler.CustomTimingIf("redis", "should show up", 5))
            {
                Thread.Sleep(10);
            }

            using (profiler.CustomTimingIf("redis", "should not show up", 15))
            {
                Thread.Sleep(10);
            }
            return Content("All good");
        }

        public IActionResult ParameterizedSqlWithEnums()
        {
            using (var conn = GetConnection())
            {
                var shouldBeOne = conn.Query<long>("select @OK = 200", new { System.Net.HttpStatusCode.OK }).Single();
                return Content("Parameterized SQL with Enums completed: " + shouldBeOne);
            }
        }

        public RedirectToActionResult MultipleRedirect() => RedirectToAction(nameof(MultipleRedirectChild));
        public RedirectToActionResult MultipleRedirectChild() => RedirectToAction(nameof(MultipleRedirectChildChild));
        public IActionResult MultipleRedirectChildChild() => Content("You should see 3 MiniProfilers from that.");

        public IActionResult ViewProfiling() => View("ForLoop");

        /// <summary>
        /// Returns an open connection that will have its queries profiled.
        /// </summary>
        /// <param name="profiler">The mini profiler.</param>
        /// <returns>the data connection abstraction.</returns>
        public DbConnection GetConnection(MiniProfiler profiler = null)
        {
            using (profiler.Step(nameof(GetConnection)))
            {
                DbConnection cnn = new SqliteConnection(Startup.SqliteConnectionString);

                // to get profiling times, we have to wrap whatever connection we're using in a ProfiledDbConnection
                // when MiniProfiler.Current is null, this connection will not record any database timings
                if (MiniProfiler.Current != null)
                {
                    cnn = new ProfiledDbConnection(cnn, MiniProfiler.Current);
                }

                cnn.Open();
                return cnn;
            }
        }

        /// <summary>
        /// Asynchronously returns an open connection that will have its queries profiled.
        /// </summary>
        /// <param name="profiler">The mini profiler.</param>
        /// <returns>the data connection abstraction.</returns>
        public async Task<DbConnection> GetConnectionAsync(MiniProfiler profiler = null)
        {
            using (profiler.Step(nameof(GetConnectionAsync)))
            {
                DbConnection cnn = new SqliteConnection(Startup.SqliteConnectionString);

                // to get profiling times, we have to wrap whatever connection we're using in a ProfiledDbConnection
                // when MiniProfiler.Current is null, this connection will not record any database timings
                if (MiniProfiler.Current != null)
                {
                    cnn = new ProfiledDbConnection(cnn, MiniProfiler.Current);
                }

                await cnn.OpenAsync().ConfigureAwait(false);
                return cnn;
            }
        }
        #endregion
    }
}
