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
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Runtime.Serialization;
    using Debug = System.Diagnostics.Debug;

    #endregion

    public delegate T CrontabFieldAccumulator<T>(int start, int end, int interval, int occurrence, T successs, Converter<ExceptionProvider, T> onError);

    [ Serializable ]
    public sealed class CrontabFieldImpl : IObjectReference
    {
        public static readonly CrontabFieldImpl Second    = new CrontabFieldImpl(CrontabFieldKind.Second, 0, 59, null, false);
        public static readonly CrontabFieldImpl Minute    = new CrontabFieldImpl(CrontabFieldKind.Minute, 0, 59, null, false);
        public static readonly CrontabFieldImpl Hour      = new CrontabFieldImpl(CrontabFieldKind.Hour, 0, 23, null, false);
        public static readonly CrontabFieldImpl Day       = new CrontabFieldImpl(CrontabFieldKind.Day, 1, 31, null, false);
        public static readonly CrontabFieldImpl Month     = new CrontabFieldImpl(CrontabFieldKind.Month, 1, 12, new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" }, false);
        public static readonly CrontabFieldImpl DayOfWeek = new CrontabFieldImpl(CrontabFieldKind.DayOfWeek, 0, 6, new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" }, true);

        static readonly CrontabFieldImpl[] FieldByKind = { Second, Minute, Hour, Day, Month, DayOfWeek };

        static readonly CompareInfo Comparer = CultureInfo.InvariantCulture.CompareInfo;

        readonly CrontabFieldKind _kind;
        readonly int _minValue;
        readonly int _maxValue;
        readonly string[] _names;
        readonly bool _occurrenceAllowed;

        public static CrontabFieldImpl FromKind(CrontabFieldKind kind)
        {
            if (!Enum.IsDefined(typeof(CrontabFieldKind), kind))
            {
                throw new ArgumentException(string.Format(
                    "Invalid crontab field kind. Valid values are {0}.",
                    string.Join(", ", Enum.GetNames(typeof(CrontabFieldKind)))), "kind");
            }

            return FieldByKind[(int) kind];
        }

        CrontabFieldImpl(CrontabFieldKind kind, int minValue, int maxValue, string[] names, bool occurrenceAllowed)
        {
            Debug.Assert(Enum.IsDefined(typeof(CrontabFieldKind), kind));
            Debug.Assert(minValue >= 0);
            Debug.Assert(maxValue >= minValue);
            Debug.Assert(names == null || names.Length == (maxValue - minValue + 1));

            _kind = kind;
            _minValue = minValue;
            _maxValue = maxValue;
            _names = names;
            _occurrenceAllowed = occurrenceAllowed;
        }

        public CrontabFieldKind Kind
        {
            get { return _kind; }
        }

        public int MinValue
        {
            get { return _minValue; }
        }

        public int MaxValue
        {
            get { return _maxValue; }
        }

        public int ValueCount
        {
            get { return _maxValue - _minValue + 1; }
        }

        public bool OccurrenceAllowed
        {
            get { return _occurrenceAllowed; }
        }

        public void Format(ICrontabField field, TextWriter writer)
        {
            Format(field, writer, false);
        }

        public void Format(ICrontabField field, TextWriter writer, bool noNames)
        {
            if (field == null)
                throw new ArgumentNullException("field");

            if (writer == null)
                throw new ArgumentNullException("writer");

            var next = field.GetFirst();
            var count = 0;

            while (next != -1)
            {
                var first = next;
                int last;

                do
                {
                    last = next;
                    next = field.Next(last + 1);
                }
                while (next - last == 1);

                if (count == 0 
                    && first == _minValue && last == _maxValue)
                {
                    writer.Write('*');
                    return;
                }
                
                if (count > 0)
                    writer.Write(',');

                if (first == last)
                {
                    FormatValue(first, writer, noNames);
                }
                else
                {
                    FormatValue(first, writer, noNames);
                    writer.Write('-');
                    FormatValue(last, writer, noNames);
                }

                count++;
            }
        }

        void FormatValue(int value, TextWriter writer, bool noNames)
        {
            Debug.Assert(writer != null);

            if (noNames || _names == null)
            {
                if (value >= 0 && value < 100)
                {
                    FastFormatNumericValue(value, writer);
                }
                else
                {
                    writer.Write(value.ToString(CultureInfo.InvariantCulture));
                }
            }
            else
            {
                var index = value - _minValue;
                writer.Write(_names[index]);
            }
        }

        static void FastFormatNumericValue(int value, TextWriter writer)
        {
            Debug.Assert(value >= 0 && value < 100);
            Debug.Assert(writer != null);

            if (value >= 10)
            {
                writer.Write((char) ('0' + (value / 10)));
                writer.Write((char) ('0' + (value % 10)));
            }
            else
            {
                writer.Write((char) ('0' + value));
            }
        }

        public void Parse(string str, CrontabFieldAccumulator<ExceptionProvider> acc)
        {
            TryParse(str, acc, null, ep => { throw ep(); });
        }
        
        public T TryParse<T>(string str, CrontabFieldAccumulator<T> acc, T success, Converter<ExceptionProvider, T> errorSelector)
        {
            if (acc == null)
                throw new ArgumentNullException("acc");

            if (string.IsNullOrEmpty(str))
                return success;

            try
            {
                return InternalParse(str, acc, success, errorSelector);
            }
            catch (FormatException e)
            {
                return OnParseException(e, str, errorSelector);
            }
            catch (CrontabException e)
            {
                return OnParseException(e, str, errorSelector);
            }
        }

        T OnParseException<T>(Exception innerException, string str, Converter<ExceptionProvider, T> errorSelector)
        {
            Debug.Assert(str != null);
            Debug.Assert(innerException != null);

            return errorSelector(
                       () => new CrontabException(string.Format("'{0}' is not a valid [{1}] crontab field expression.", str, Kind), innerException));
        }

        T InternalParse<T>(string str, CrontabFieldAccumulator<T> acc, T success, Converter<ExceptionProvider, T> errorSelector)
        {
            Debug.Assert(str != null);
            Debug.Assert(acc != null);

            if (str.Length == 0)
                return errorSelector(() => new CrontabException("A crontab field value cannot be empty."));

            //
            // Next, look for a list of values (e.g. 1,2,3).
            //
    
            var commaIndex = str.IndexOf(",");

            if (commaIndex > 0)
            {
                var result = success;
                var token = ((IEnumerable<string>) str.Split(StringSeparatorStock.Comma)).GetEnumerator();
                while (token.MoveNext() && result == null)
                    result = InternalParse(token.Current, acc, success, errorSelector);
                return result;
            }

            var every = 1;
            var occurrence = 0;

            //
            // Look for stepping first (e.g. */2 = every 2nd).
            // 

            var slashIndex = str.IndexOf("/");

            if (slashIndex > 0)
            {
                every = int.Parse(str.Substring(slashIndex + 1), CultureInfo.InvariantCulture);
                str = str.Substring(0, slashIndex);
            }

            //
            // Look for occurrence order (e.g. 1#2 = every 2nd monday of month).
            // 

            var hashIndex = str.IndexOf("#");

            if (hashIndex > 0)
            {
                occurrence = int.Parse(str.Substring(hashIndex + 1), CultureInfo.InvariantCulture);
                str = str.Substring(0, hashIndex);
            }

            //
            // Next, look for wildcard (*).
            //
    
            if (str.Length == 1 && str[0]== '*')
            {
                return acc(-1, -1, every, occurrence, success, errorSelector);
            }

            //
            // Next, look for a range of values (e.g. 2-10).
            //

            var dashIndex = str.IndexOf("-");
        
            if (dashIndex > 0)
            {
                var first = ParseValue(str.Substring(0, dashIndex));
                var last = ParseValue(str.Substring(dashIndex + 1));

                return acc(first, last, every, occurrence, success, errorSelector);
            }

            //
            // Finally, handle the case where there is only one number.
            //

            var value = ParseValue(str);

            if (every == 1)
                return acc(value, value, 1, occurrence, success, errorSelector);

            Debug.Assert(every != 0);
            return acc(value, _maxValue, every, occurrence, success, errorSelector);
        }

        int ParseValue(string str)
        {
            Debug.Assert(str != null);

            if (str.Length == 0)
                throw new CrontabException("A crontab field value cannot be empty.");

            var firstChar = str[0];
        
            if (firstChar >= '0' && firstChar <= '9')
                return int.Parse(str, CultureInfo.InvariantCulture);

            if (_names == null)
            {
                throw new CrontabException(string.Format(
                    "'{0}' is not a valid [{3}] crontab field value. It must be a numeric value between {1} and {2} (all inclusive).",
                    str, _minValue.ToString(), _maxValue.ToString(), _kind.ToString()));
            }

            for (var i = 0; i < _names.Length; i++)
            {
                if (Comparer.IsPrefix(_names[i], str, CompareOptions.IgnoreCase))
                    return i + _minValue;
            }

            throw new CrontabException(string.Format(
                "'{0}' is not a known value name. Use one of the following: {1}.", 
                str, string.Join(", ", _names)));
        }

        object IObjectReference.GetRealObject(StreamingContext context)
        {
            return FromKind(Kind);
        }
    }
}
