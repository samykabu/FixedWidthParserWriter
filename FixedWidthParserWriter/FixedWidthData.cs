﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
//using FastMember; // Nuget FastMember.Signed

namespace FixedWidthParserWriter
{
    public class FixedWidthData
    {
        public List<string> Content { get; set; }
        public DefaultConfig DefaultConfig { get; set; } = new DefaultConfig();

        public virtual void SetDefaultConfig() { } // to be overriden for setting custom Format on entire property type

        protected T ParseData<T>(List<string> lines, FieldType fieldType, int structureTypeId = 0) where T : class, new()
        {
            SetDefaultConfig();

            var data = new T();
            var orderProperties = data.GetType().GetProperties().Where(a => Attribute.IsDefined(a, typeof(FixedWidthAttribute))).ToList();

            //var accessor = TypeAccessor.Create(typeof(T), true); // with FastMember
            foreach (var property in orderProperties)
            {
                if (!property.CanWrite)
                    continue;

                FixedWidthAttribute field = null;
                int lineIndex = 0;
                if (fieldType == FieldType.LineField)
                {
                    field = property.GetCustomAttributes<FixedWidthLineFieldAttribute>().SingleOrDefault(a => a.StructureTypeId == structureTypeId);
                }
                else if (fieldType == FieldType.FileField)
                {
                    field = property.GetCustomAttributes<FixedWidthFileFieldAttribute>().SingleOrDefault(a => a.StructureTypeId == structureTypeId);
                    // Deprecated from when every StructureType had all Properties and those that did not had it's specific attribute took the one with higest StructureTypeId
                    //field = property.GetCustomAttributes<FixedWidthFileFieldAttribute>().Where(a => a.StructureTypeId <= structureTypeId).OrderByDescending(a => a.StructureTypeId).FirstOrDefault();

                    var fileField = (FixedWidthFileFieldAttribute)field;
                    if (fileField.Line == 0)
                        throw new InvalidOperationException("'Line' parameter of [FixedWidthFileFieldAttribute] can not be zero.");
                    lineIndex = fileField.LineIndex;
                    if (lineIndex < 0) // line is counted from bottom
                    {
                        lineIndex += lines.Count + 1;
                    }
                }
                if (field != null)
                {
                    string valueString = lines[lineIndex];

                    int startIndex = field.StartIndex;
                    int length = field.Length;
                    if (startIndex > 0 || length > 0) // Length = 0; means value is entire line
                    {
                        if (valueString.Length < startIndex + length)
                        {
                            throw new InvalidOperationException($"Property {property.Name}='{valueString}' with Length={valueString.Length} not enough for Substring(Start={startIndex + 1}, Length={length})");
                        }
                        valueString = (length == 0) ? valueString.Substring(startIndex) : valueString.Substring(startIndex, length);
                    }

                    valueString = valueString.Trim();

                    object value = null;
                    var underlyingType = Nullable.GetUnderlyingType(property.PropertyType);
                    string valueTypeName = underlyingType != null ? underlyingType.Name : property.PropertyType.Name;

                    if (valueTypeName == nameof(String))
                    {
                        value = valueString;
                    }
                    else if(valueTypeName == nameof(Char))
                    {
                        value = (char)valueString[0];
                    }
                    else
                    {
                        string format = field.Format; //= field.Format ?? DefaultFormat.GetFormat(valueTypeName);

                        if (valueTypeName == nameof(Int32) || valueTypeName == nameof(Int64))
                        {
                            format = format ?? DefaultConfig.FormatNumberInteger;
                            switch (valueTypeName)
                            {
                                case nameof(Int32):
                                    value = Int32.Parse(valueString);
                                    break;
                                case nameof(Int64):
                                    value = Int64.Parse(valueString);
                                    break;
                            }
                        }
                        else if (valueTypeName == nameof(Decimal) || valueTypeName == nameof(Single) || valueTypeName == nameof(Double))
                        {
                            format = format ?? DefaultConfig.FormatNumberDecimal;
                            switch (valueTypeName)
                            {
                                case nameof(Decimal):
                                    value = Decimal.Parse(valueString, CultureInfo.InvariantCulture);
                                    if (format.Contains(";")) //';' - Special custom Format that removes decimal separator ("0;00": 123.45 -> 12345)
                                        value = (decimal)value / (decimal)Math.Pow(10, format.Length - 2); // "0;00".Length == 4 - 2 = 2 (10^2 = 100)
                                    break;
                                case nameof(Single):
                                    value = Single.Parse(valueString, CultureInfo.InvariantCulture);
                                    if (format.Contains(";"))
                                        value = (float)value / (float)Math.Pow(10, format.Length - 2);
                                    break;
                                case nameof(Double):
                                    value = Double.Parse(valueString, CultureInfo.InvariantCulture);
                                    if (format.Contains(";"))
                                        value = (double)value / (double)Math.Pow(10, format.Length - 2);
                                    break;
                            }
                        }
                        else if(valueTypeName == nameof(Boolean))
                        {
                            format = format ?? DefaultConfig.FormatBoolean;
                        }
                        else if (valueTypeName == nameof(DateTime))
                        {
                            format = format ?? DefaultConfig.FormatDateTime;
                            value = DateTime.ParseExact(valueString, format, CultureInfo.InvariantCulture);
                        }
                    }
                    property.SetValue(data, value);
                    //accessor[data, property.Name] = value; // with FastMember
                }
            }
            return data;
        }

        protected string ToFormatedString(PropertyInfo property, FieldType fieldType, int structureTypeId = 0)
        {
            FixedWidthAttribute field = null;
            if (fieldType == FieldType.LineField)
            {
                field = property.GetCustomAttributes<FixedWidthLineFieldAttribute>().SingleOrDefault(a => a.StructureTypeId == structureTypeId);
            }
            else if (fieldType == FieldType.FileField)
            {
                field = property.GetCustomAttributes<FixedWidthFileFieldAttribute>().SingleOrDefault(a => a.StructureTypeId == structureTypeId);
            }
            var value = property.GetValue(this);
            //var accessor = TypeAccessor.Create(this.GetType()); // move before in caller method before loop
            //var value = accessor[this, property.Name];
            
            string valueTypeName = value?.GetType().Name ?? nameof(String);

            if (valueTypeName == nameof(Int32) || valueTypeName == nameof(Int64) || valueTypeName == nameof(Decimal) || valueTypeName == nameof(Single) || valueTypeName == nameof(Double))
            {
                field.PadSide = field.PadSide != PadSide.Default ? field.PadSide : DefaultConfig.PadSideNumeric; // Numeric types have initial default Left pad
            }
            else
            {
                field.PadSide = field.PadSide != PadSide.Default ? field.PadSide : DefaultConfig.PadSideNonNumeric; // NonNumeric types have initial default Right pad
            }

            char pad = ' ';

            string result = String.Empty;
            if (valueTypeName == nameof(String) || valueTypeName == nameof(Char))
            {
                result = value?.ToString() ?? "";
            }
            else
            {
                string format = field.Format;
                //format = format ?? DefaultFormat.GetFormat(valueTypeName);
                switch (valueTypeName)
                {
                    case nameof(Int32):
                    case nameof(Int64):
                        format = format ?? DefaultConfig.FormatNumberInteger;
                        pad = field.Pad != '\0' ? field.Pad : DefaultConfig.PadSeparatorNumeric;
                        break;
                    case nameof(Decimal):
                    case nameof(Single):
                    case nameof(Double):
                        format = format ?? DefaultConfig.FormatNumberDecimal;
                        pad = field.Pad != '\0' ? field.Pad : DefaultConfig.PadSeparatorNumeric;
                        if (format.Contains(";")) //';' - Special custom Format that removes decimal separator ("0;00": 123.45 -> 12345)
                        {
                            double decimalFactor = Math.Pow(10, format.Length - 2); // "0;00".Length == 4 - 2 = 2 (10^2 = 100)
                            switch (valueTypeName)
                            {
                                case nameof(Decimal):
                                    value = (decimal)value * (decimal)decimalFactor;
                                    break;
                                case nameof(Single):
                                    value = (float)value * (float)decimalFactor;
                                    break;
                                case nameof(Double):
                                    value = (double)value * decimalFactor;
                                    break;
                            }
                        }
                        break;
                    case nameof(Boolean):
                        format = format ?? DefaultConfig.FormatBoolean;
                        pad = field.Pad != '\0' ? field.Pad : DefaultConfig.PadSeparatorNonNumeric;
                        value = value.GetHashCode();
                        break;
                    case nameof(DateTime):
                        format = format ?? DefaultConfig.FormatDateTime;
                        pad = field.Pad != '\0' ? field.Pad : DefaultConfig.PadSeparatorNonNumeric;
                        break;
                }
                result = format != null ? String.Format(CultureInfo.InvariantCulture, $"{{0:{format}}}", value) : value.ToString();
            }

            if (result.Length > field.Length && field.Length > 0) // if too long cut it
            {
                result = result.Substring(0, field.Length);
            }
            else if (result.Length < field.Length) // if too short pad from one side
            {
                if (field.PadSide == PadSide.Right)
                    result = result.PadRight(field.Length, pad);
                else if (field.PadSide == PadSide.Left)
                    result = result.PadLeft(field.Length, pad);
            }


            if (fieldType == FieldType.FileField)
            {
                int lineIndex = ((FixedWidthFileFieldAttribute)field).LineIndex;
                var content = this.Content;
                if (lineIndex < 0)
                {
                    lineIndex += content.Count + 1;
                }
                string clearString = String.Empty;
                clearString = field.Length > 0 ? content[lineIndex].Remove(field.StartIndex, field.Length) : content[lineIndex].Remove(field.StartIndex);
                content[lineIndex] = clearString.Insert(field.StartIndex, result);
            }
            return result;
        }
    }
}
