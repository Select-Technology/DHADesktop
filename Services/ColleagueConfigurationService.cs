using DHA.DSTC.WPF.DataAccess;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;

namespace DHA.DSTC.WPF.Services
{
    public class ColleagueConfigurationService
    {
        private readonly DataverseConnector _connector;

        public ColleagueConfigurationService(DataverseConnector connector)
        {
            _connector = connector;
        }

        public ColleagueConfiguration GetColleagueConfiguration(Guid systemUserId)
        {
            try
            {
                _connector.Connect();

                var query = new QueryExpression("fwp_colleagueconfiguration")
                {
                    ColumnSet = new ColumnSet(
                        "fwp_mondayexpectedhours",
                        "fwp_tuesdayexpectedhours",
                        "fwp_wednesdayexpectedhours",
                        "fwp_thursdayexpectedhours",
                        "fwp_fridayexpectedhours",
                        "fwp_saturdayexpectedhours",
                        "fwp_sundayexpectedhours",
                        "fwp_colleague"
                    ),
                    Criteria = new FilterExpression()
                };

                query.Criteria.AddCondition("fwp_colleague", ConditionOperator.Equal, systemUserId);

                var result = _connector._orgService.RetrieveMultiple(query);

                if (result.Entities.Count > 0)
                {
                    return ColleagueConfiguration.FromEntity(result.Entities[0]);
                }

                // Return default 8-hour configuration if none found
                return ColleagueConfiguration.CreateDefault();
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show($"Error retrieving colleague configuration: {ex.Message}",
                    "Data Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return ColleagueConfiguration.CreateDefault();
            }
        }
    }

    public class ColleagueConfiguration
    {
        public decimal MondayExpectedHours { get; set; }
        public decimal TuesdayExpectedHours { get; set; }
        public decimal WednesdayExpectedHours { get; set; }
        public decimal ThursdayExpectedHours { get; set; }
        public decimal FridayExpectedHours { get; set; }
        public decimal SaturdayExpectedHours { get; set; }
        public decimal SundayExpectedHours { get; set; }

        public decimal GetExpectedHoursForDay(DayOfWeek dayOfWeek)
        {
            switch (dayOfWeek)
            {
                case DayOfWeek.Monday:
                    return MondayExpectedHours;
                case DayOfWeek.Tuesday:
                    return TuesdayExpectedHours;
                case DayOfWeek.Wednesday:
                    return WednesdayExpectedHours;
                case DayOfWeek.Thursday:
                    return ThursdayExpectedHours;
                case DayOfWeek.Friday:
                    return FridayExpectedHours;
                case DayOfWeek.Saturday:
                    return SaturdayExpectedHours;
                case DayOfWeek.Sunday:
                    return SundayExpectedHours;
                default:
                    return 0;
            }
        }

        public static ColleagueConfiguration FromEntity(Entity entity)
        {
            if (entity == null) return CreateDefault();

            return new ColleagueConfiguration
            {
                MondayExpectedHours = entity.GetAttributeValue<decimal>("fwp_mondayexpectedhours"),
                TuesdayExpectedHours = entity.GetAttributeValue<decimal>("fwp_tuesdayexpectedhours"),
                WednesdayExpectedHours = entity.GetAttributeValue<decimal>("fwp_wednesdayexpectedhours"),
                ThursdayExpectedHours = entity.GetAttributeValue<decimal>("fwp_thursdayexpectedhours"),
                FridayExpectedHours = entity.GetAttributeValue<decimal>("fwp_fridayexpectedhours"),
                SaturdayExpectedHours = entity.GetAttributeValue<decimal>("fwp_saturdayexpectedhours"),
                SundayExpectedHours = entity.GetAttributeValue<decimal>("fwp_sundayexpectedhours")
            };
        }

        public static ColleagueConfiguration CreateDefault()
        {
            return new ColleagueConfiguration
            {
                MondayExpectedHours = 8,
                TuesdayExpectedHours = 8,
                WednesdayExpectedHours = 8,
                ThursdayExpectedHours = 8,
                FridayExpectedHours = 8,
                SaturdayExpectedHours = 0,
                SundayExpectedHours = 0
            };
        }
    }

    public static class HolidayCalculator
    {
        private static readonly TimeSpan LunchStart = new TimeSpan(13, 0, 0); // 13:00
        private static readonly TimeSpan LunchEnd = new TimeSpan(14, 0, 0);   // 14:00
        private static readonly TimeSpan LunchDuration = TimeSpan.FromHours(1);

        /// <summary>
        /// Calculates holiday hours based on overlap with expected work hours, accounting for lunch break
        /// </summary>
        /// <param name="startDateTime">Holiday start date and time</param>
        /// <param name="endDateTime">Holiday end date and time</param>
        /// <param name="colleagueConfig">User's expected hours configuration</param>
        /// <returns>Total holiday hours to record</returns>
        public static decimal CalculateHolidayHours(DateTime startDateTime, DateTime endDateTime, ColleagueConfiguration colleagueConfig)
        {
            if (startDateTime >= endDateTime)
                return 0;

            decimal totalHours = 0;
            var currentDate = startDateTime.Date;

            while (currentDate <= endDateTime.Date)
            {
                var expectedHours = colleagueConfig.GetExpectedHoursForDay(currentDate.DayOfWeek);

                if (expectedHours > 0)
                {
                    // Calculate overlap for this day
                    var dayStart = currentDate == startDateTime.Date ? startDateTime : currentDate.Add(new TimeSpan(9, 0, 0)); // Assume 9am start
                    var dayEnd = currentDate == endDateTime.Date ? endDateTime : currentDate.Add(new TimeSpan(17, 0, 0)); // Assume 5pm end

                    // Ensure we don't go beyond the working day
                    var workStart = currentDate.Add(new TimeSpan(9, 0, 0));
                    var workEnd = currentDate.Add(new TimeSpan(9, 0, 0)).AddHours((double)expectedHours).Add(LunchDuration); // Add lunch to get actual end time

                    dayStart = dayStart < workStart ? workStart : dayStart;
                    dayEnd = dayEnd > workEnd ? workEnd : dayEnd;

                    if (dayStart < dayEnd)
                    {
                        var holidayDuration = dayEnd - dayStart;

                        // Check if lunch period overlaps with holiday
                        var lunchStart = currentDate.Add(LunchStart);
                        var lunchEnd = currentDate.Add(LunchEnd);

                        if (dayStart < lunchEnd && dayEnd > lunchStart)
                        {
                            // Holiday overlaps lunch - subtract lunch duration
                            holidayDuration = holidayDuration.Subtract(LunchDuration);
                        }

                        totalHours += Math.Max(0, (decimal)holidayDuration.TotalHours);
                    }
                }

                currentDate = currentDate.AddDays(1);
            }

            return Math.Round(totalHours, 2);
        }
    }
}