using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Samples.AspNetCore.Models;
using StackExchange.Profiling;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace Samples.AspNetCore.Controllers
{
    public class Test4Controller : Controller
    {
        private readonly MiniProfilerOptions _miniProfilerOptions;
        private readonly string _dbConnectionString;
        private readonly ILogger _logger;
        public static Guid LastUpdate;

        public Test4Controller(IOptions<MiniProfilerOptions> options, IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            _miniProfilerOptions = options.Value;
            _dbConnectionString = configuration.GetConnectionString("MyDbConnection");
            _logger = loggerFactory.CreateLogger<Test4Controller>();
        }

        private const string getAllQuery = "SELECT x.Id FROM RouteHits as x";

        [HttpGet]
        public async Task<IActionResult> GetHitListAsync()
        {
            if (!ModelState.IsValid) return BadRequest();

            LastUpdate = MiniProfiler.Current.Id;

            using (MiniProfiler.Current.Step("EF Core Stuff"))
            {
                using (SqlConnection connection = new SqlConnection(_dbConnectionString))
                {
                    using (MiniProfiler.Current.Step("Open connection"))
                    {
                        await connection.OpenAsync().ConfigureAwait(false);
                    }

                    using (MiniProfiler.Current.Step("Get items"))
                    {
                        SqlCommand command = new SqlCommand(getAllQuery, connection);
                        SqlDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

                        List<Guid> output = new List<Guid>();

                        while (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            output.Add(Guid.Parse(reader[0].ToString()));
                        }

                        reader.Close();

                        return Ok(output);
                    }
                }
            }
        }

        private const string get1Query = "SELECT TOP(1) [h].[Id], [h].[Name], [h].[UpdateTime] "
                        + "FROM[RouteHits] AS[h] "
                        + "WHERE[h].[Id] = @id";
        private const string update1Query = "UPDATE [RouteHits] SET [UpdateTime] = @now "
                        + "WHERE[Id] = @id";

        [HttpGet]
        public async Task<IActionResult> UpdateItemAsync(Guid id)
        {
            Random r = new Random();
            bool todo = r.NextDouble() > 0.5;

            using (MiniProfiler.Current.Step("EF Core Stuff"))
            {
                using (MiniProfiler.Current.Step("Get Existing"))
                {
                    using (SqlConnection connection = new SqlConnection(_dbConnectionString))
                    {
                        SqlCommand command = new SqlCommand(get1Query, connection);
                        command.Parameters.AddWithValue("@id", id);

                        SqlDataReader reader = default(SqlDataReader);

                        using (MiniProfiler.Current.Step("Open connection"))
                        {
                            if (todo)
                            {
                                await connection.OpenAsync().ConfigureAwait(false);
                            }
                        }

                        using (MiniProfiler.Current.Step("Execute query"))
                        {
                            if (todo)
                                reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                        }

                        using (MiniProfiler.Current.Step("Read data"))
                        {
                            if (todo)
                            {
                                while (await reader.ReadAsync().ConfigureAwait(false))
                                {
                                    RouteHit rh = new RouteHit
                                    {
                                        Id = reader.GetGuid(0),
                                        Name = reader.GetString(1),
                                        UpdateTime = reader.GetDateTime(2)
                                    };
                                    _logger.LogInformation($"RH: {rh.Id} {rh.Name} {rh.UpdateTime}");
                                }
                                reader.Close();
                            }
                        }
                    }
                }

                using (MiniProfiler.Current.Step("Update"))
                {
                    using (SqlConnection connection = new SqlConnection(_dbConnectionString))
                    {
                        SqlCommand command = new SqlCommand(update1Query, connection);
                        command.Parameters.AddWithValue("@id", id);
                        command.Parameters.AddWithValue("@now", DateTime.UtcNow);

                        SqlDataReader reader = default(SqlDataReader);

                        using (MiniProfiler.Current.Step("Open connection"))
                        {
                            if (!todo)
                            {
                                await connection.OpenAsync().ConfigureAwait(false);
                            }
                        }

                        using (MiniProfiler.Current.Step("Execute query"))
                        {
                            if (!todo)
                            {
                                reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                            }
                        }

                        using (MiniProfiler.Current.Step("Read data"))
                        {
                            if (!todo)
                            {
                                while (await reader.ReadAsync().ConfigureAwait(false))
                                {
                                    _logger.LogInformation($"Update: {reader.HasRows} - {reader.RecordsAffected}");
                                }
                                reader.Close();
                            }
                        }
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
                var getSql = get.Children;

                var update = item.Root.Children[1].Children[0].Children[1];
                var updateSql = update.Children;

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
