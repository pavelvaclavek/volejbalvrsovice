using System;
using System.Data;
using System.Globalization;
using Dapper;
using VolejbalVrsovice.Core;

namespace VolejbalVrsovice.Data.Db
{
    /// <summary>
    /// Jednorázová konfigurace Dapperu: peníze, datum a čas se do SQLite ukládají ve vlastních formátech
    /// (viz zadání, sekce 2). Aby se handlery použily i pro parametry dotazů, je potřeba Dapperu odebrat
    /// jeho výchozí mapování těchto typů.
    /// </summary>
    internal static class DapperSetup
    {
        private static readonly object Gate = new object();
        private static bool _configured;

        public static void EnsureConfigured()
        {
            lock (Gate)
            {
                if (_configured)
                {
                    return;
                }

                foreach (var type in new[]
                {
                    typeof(decimal), typeof(decimal?),
                    typeof(DateTime), typeof(DateTime?),
                    typeof(TimeSpan), typeof(TimeSpan?),
                })
                {
                    SqlMapper.RemoveTypeMap(type);
                }

                SqlMapper.AddTypeHandler(new MoneyTypeHandler());
                SqlMapper.AddTypeHandler(new DateTimeTypeHandler());
                SqlMapper.AddTypeHandler(new TimeOfDayTypeHandler());

                _configured = true;
            }
        }
    }

    /// <summary><see cref="decimal"/> (Kč) ↔ INTEGER (haléře). Všechny hodnoty typu decimal v aplikaci jsou peníze.</summary>
    internal sealed class MoneyTypeHandler : SqlMapper.TypeHandler<decimal>
    {
        public override void SetValue(IDbDataParameter parameter, decimal value)
        {
            parameter.DbType = DbType.Int64;
            parameter.Value = Money.ToHalere(value);
        }

        public override decimal Parse(object value)
        {
            return Money.FromHalere(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }
    }

    /// <summary><see cref="DateTime"/> ↔ TEXT; datum bez času <c>yyyy-MM-dd</c>, okamžik v UTC <c>yyyy-MM-ddTHH:mm:ssZ</c>.</summary>
    internal sealed class DateTimeTypeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = DbFormats.FormatDateTime(value);
        }

        public override DateTime Parse(object value)
        {
            return DbFormats.ParseDateTime(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
    }

    /// <summary><see cref="TimeSpan"/> (čas tréninku) ↔ TEXT <c>HH:mm</c>.</summary>
    internal sealed class TimeOfDayTypeHandler : SqlMapper.TypeHandler<TimeSpan>
    {
        public override void SetValue(IDbDataParameter parameter, TimeSpan value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = DbFormats.FormatTimeOfDay(value);
        }

        public override TimeSpan Parse(object value)
        {
            return DbFormats.ParseTimeOfDay(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
    }
}
