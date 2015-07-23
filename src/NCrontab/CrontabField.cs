#region License and Terms
//
// NCrontab - Crontab for .NET
// Copyright (c) 2008 Atif Aziz. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#endregion

namespace NCrontab
{
    #region Imports

    using System;
    using System.Collections;
    using System.Globalization;
    using System.IO;

    #endregion

    /// <summary>
    /// Represents a single crontab field.
    /// </summary>

    [Serializable]
    public sealed class CrontabField : ICrontabField
    {
        internal readonly BitArray Bits;
        /* readonly */
        int _minValueSet;
        /* readonly */
        int _maxValueSet;
        /* readonly */
        int _occurrence;
        readonly CrontabFieldImpl _impl;
        public int? Every { get; private set; }

        /// <summary>
        /// Parses a crontab field expression given its kind.
        /// </summary>

        public static CrontabField Parse(CrontabFieldKind kind, string expression)
        {
            return TryParse(kind, expression, v => v, e => { throw e(); });
        }

        public static CrontabField TryParse(CrontabFieldKind kind, string expression)
        {
            return TryParse(kind, expression, v => v, _ => null);
        }

        public static T TryParse<T>(CrontabFieldKind kind, string expression, Converter<CrontabField, T> valueSelector, Converter<ExceptionProvider, T> errorSelector)
        {
            var field = new CrontabField(CrontabFieldImpl.FromKind(kind));
            var error = field._impl.TryParse(expression, field.Accumulate, null, e => e);
            return error == null ? valueSelector(field) : errorSelector(error);
        }

        /// <summary>
        /// Parses a crontab field expression representing seconds.
        /// </summary>

        public static CrontabField Seconds(string expression)
        {
            return Parse(CrontabFieldKind.Second, expression);
        }

        /// <summary>
        /// Parses a crontab field expression representing minutes.
        /// </summary>

        public static CrontabField Minutes(string expression)
        {
            return Parse(CrontabFieldKind.Minute, expression);
        }

        /// <summary>
        /// Parses a crontab field expression representing hours.
        /// </summary>

        public static CrontabField Hours(string expression)
        {
            return Parse(CrontabFieldKind.Hour, expression);
        }

        /// <summary>
        /// Parses a crontab field expression representing days in any given month.
        /// </summary>

        public static CrontabField Days(string expression)
        {
            return Parse(CrontabFieldKind.Day, expression);
        }

        /// <summary>
        /// Parses a crontab field expression representing months.
        /// </summary>

        public static CrontabField Months(string expression)
        {
            return Parse(CrontabFieldKind.Month, expression);
        }

        /// <summary>
        /// Parses a crontab field expression representing days of a week.
        /// </summary>

        public static CrontabField DaysOfWeek(string expression)
        {
            return Parse(CrontabFieldKind.DayOfWeek, expression);
        }

        CrontabField(CrontabFieldImpl impl)
        {
            if (impl == null)
                throw new ArgumentNullException("impl");

            _impl = impl;
            Bits = new BitArray(impl.ValueCount);

            Bits.SetAll(false);
            _minValueSet = int.MaxValue;
            _maxValueSet = -1;
            _occurrence = 0;
        }

        /// <summary>
        /// Gets the first value of the field or -1.
        /// </summary>

        public int GetFirst()
        {
            return _minValueSet < int.MaxValue ? _minValueSet : -1;
        }

        /// <summary>
        /// Gets the next value of the field that occurs after the given 
        /// start value or -1 if there is no next value available.
        /// </summary>

        public int Next(int start)
        {
            if (start < _minValueSet)
                return _minValueSet;

            var startIndex = ValueToIndex(start);
            var lastIndex = ValueToIndex(_maxValueSet);

            for (var i = startIndex; i <= lastIndex; i++)
            {
                if (Bits[i])
                    return IndexToValue(i);
            }

            return -1;
        }

        int IndexToValue(int index)
        {
            return index + _impl.MinValue;
        }

        int ValueToIndex(int value)
        {
            return value - _impl.MinValue;
        }

        /// <summary>
        /// Determines if the given value occurs in the field.
        /// </summary>

        public bool Contains(int value)
        {
            return Bits[ValueToIndex(value)];
        }

        /// <summary>
        /// Determines if the given date matche with the field.
        /// </summary>

        public bool Match(DateTime dateTime)
        {
            var contains = Contains(GetValue(_impl.Kind, dateTime));
            if (contains && _occurrence > 0)
            {
                return _occurrence == Occurrence(dateTime);
            }
            return contains;
        }

        /// <summary>
        /// Determines if the given date matche with the field.
        /// </summary>

        public bool Match(DateTime startDate, DateTime dateTime)
        {
            if (Every.HasValue == false)
                return Match(dateTime);

            while (startDate <= dateTime)
            {
                startDate = Next(_impl.Kind, startDate, Every.Value);
            }

            startDate = Next(_impl.Kind, startDate, -Every.Value);

            return Match(_impl.Kind, startDate, dateTime);
        }

        private static int GetValue(CrontabFieldKind kind, DateTime dateTime)
        {
            switch (kind)
            {
                case CrontabFieldKind.Second:
                    return dateTime.Second;
                case CrontabFieldKind.Minute:
                    return dateTime.Minute;
                case CrontabFieldKind.Hour:
                    return dateTime.Hour;
                case CrontabFieldKind.Day:
                    return dateTime.Day;
                case CrontabFieldKind.Month:
                    return dateTime.Month;
                case CrontabFieldKind.DayOfWeek:
                    return (int)dateTime.DayOfWeek;
            }

            return -1;
        }

        private static bool Match(CrontabFieldKind kind, DateTime startDate, DateTime dateTime)
        {
            return GetValue(kind, startDate) == GetValue(kind, dateTime);
        }

        private static DateTime Next(CrontabFieldKind kind, DateTime dateTime, int interval)
        {
            switch (kind)
            {
                case CrontabFieldKind.Second:
                    return dateTime.AddSeconds(interval);
                case CrontabFieldKind.Minute:
                    return dateTime.AddMinutes(interval);
                case CrontabFieldKind.Hour:
                    return dateTime.AddHours(interval);
                default:
                case CrontabFieldKind.Day:
                    return dateTime.AddDays(interval);
                case CrontabFieldKind.Month:
                    return dateTime.AddMonths(interval);
                case CrontabFieldKind.DayOfWeek:
                    return dateTime.AddDays(interval * 7);
            }
        }

        /// <summary>
        /// Get day of week occurrence in the month
        /// </summary>

        static int Occurrence(DateTime dateTime)
        {
            return (int)Math.Ceiling(dateTime.Day / 7.0);
        }

        /// <summary>
        /// Accumulates the given range (start to end) and interval of values
        /// into the current set of the field.
        /// </summary>
        /// <remarks>
        /// To set the entire range of values representable by the field,
        /// set <param name="start" /> and <param name="end" /> to -1 and
        /// <param name="interval" /> to 1.
        /// </remarks>

        T Accumulate<T>(int start, int end, int interval, int occurrence, T success, Converter<ExceptionProvider, T> errorSelector)
        {
            if (occurrence > 0 && !_impl.OccurrenceAllowed)
                return OnOccurrenceNotAllowed(errorSelector);

            var minValue = _impl.MinValue;
            var maxValue = _impl.MaxValue;
            _occurrence = occurrence;
            Every = interval <= 1 ? (int?)null : interval;

            if (start == end)
            {
                if (start < 0)
                {
                    //
                    // We're setting the entire range of values.
                    //

                    if (interval <= 1)
                    {
                        _minValueSet = minValue;
                        _maxValueSet = maxValue;
                        Bits.SetAll(true);
                        return success;
                    }

                    start = minValue;
                    end = maxValue;
                }
                else
                {
                    //
                    // We're only setting a single value - check that it is in range.
                    //

                    if (start < minValue)
                        return OnValueBelowMinError(start, errorSelector);

                    if (start > maxValue)
                        return OnValueAboveMaxError(start, errorSelector);
                }
            }
            else
            {
                //
                // For ranges, if the start is bigger than the end value then
                // swap them over.
                //

                if (start > end)
                {
                    end ^= start;
                    start ^= end;
                    end ^= start;
                }

                if (start < 0)
                    start = minValue;
                else if (start < minValue)
                    return OnValueBelowMinError(start, errorSelector);

                if (end < 0)
                    end = maxValue;
                else if (end > maxValue)
                    return OnValueAboveMaxError(end, errorSelector);
            }

            if (interval < 1)
                interval = 1;

            int i;

            //
            // Populate the _bits table by setting all the bits corresponding to
            // the valid field values.
            //

            for (i = start - minValue; i <= (end - minValue); i += interval)
                Bits[i] = true;

            //
            // Make sure we remember the minimum value set so far Keep track of
            // the highest and lowest values that have been added to this field
            // so far.
            //

            if (_minValueSet > start)
                _minValueSet = start;

            i += (minValue - interval);

            if (_maxValueSet < i)
                _maxValueSet = i;

            return success;
        }

        T OnValueAboveMaxError<T>(int value, Converter<ExceptionProvider, T> errorSelector)
        {
            return errorSelector(
                () => new CrontabException(string.Format(
                    "{0} is higher than the maximum allowable value for the [{3}] field. Value must be between {1} and {2} (all inclusive).",
                    value, _impl.MinValue, _impl.MaxValue, _impl.Kind)));
        }

        T OnValueBelowMinError<T>(int value, Converter<ExceptionProvider, T> errorSelector)
        {
            return errorSelector(
                () => new CrontabException(string.Format(
                    "{0} is lower than the minimum allowable value for the [{3}] field. Value must be between {1} and {2} (all inclusive).",
                    value, _impl.MinValue, _impl.MaxValue, _impl.Kind)));
        }

        T OnOccurrenceNotAllowed<T>(Converter<ExceptionProvider, T> errorSelector)
        {
            return errorSelector(
                () => new CrontabException(string.Format(
                    "For the [{0}] field occurrence (#) is not allowed.",
                    _impl.Kind)));
        }

        public override string ToString()
        {
            return ToString(null);
        }

        public string ToString(string format)
        {
            var writer = new StringWriter(CultureInfo.InvariantCulture);

            switch (format)
            {
                case "G":
                case null:
                    Format(writer, true);
                    break;
                case "N":
                    Format(writer);
                    break;
                default:
                    throw new FormatException();
            }

            return writer.ToString();
        }

        public void Format(TextWriter writer)
        {
            Format(writer, false);
        }

        public void Format(TextWriter writer, bool noNames)
        {
            _impl.Format(this, writer, noNames);
        }
    }
}
