/////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Tencent is pleased to support the open source community by making behaviac available.
//
// Copyright (C) 2015 THL A29 Limited, a Tencent company. All rights reserved.
//
// Licensed under the BSD 3-Clause License (the "License"); you may not use this file except in compliance with
// the License. You may obtain a copy of the License at http://opensource.org/licenses/BSD-3-Clause
//
// Unless required by applicable law or agreed to in writing, software distributed under the License is
// distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and limitations under the License.
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Reflection;

namespace Behaviac.Design.Exporters
{
    public class BsonException : Exception
    {
        public BsonException() { }
        public BsonException(string message) : base(message) { }
        public BsonException(string message, Exception innerException) : base(message, innerException) { }
        protected BsonException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    internal class TypeHelper
    {
        private static readonly IDictionary<Type, TypeHelper> _cachedTypeLookup = new Dictionary<Type, TypeHelper>();
        private readonly IDictionary<string, PropertyInfo> _properties = new Dictionary<string, PropertyInfo>();

        private TypeHelper(Type type)
        {
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            foreach (PropertyInfo p in properties)
            {
                _properties[p.Name] = p;
            }
        }

        public ICollection<PropertyInfo> GetProperties()
        {
            return _properties.Values;
        }

        public PropertyInfo FindProperty(string name)
        {
            return _properties.ContainsKey(name) ? _properties[name] : null;
        }

        public static TypeHelper GetHelperForType(Type type)
        {
            TypeHelper helper;
            if (!_cachedTypeLookup.TryGetValue(type, out helper))
            {
                helper = new TypeHelper(type);
                _cachedTypeLookup[type] = helper;
            }
            return helper;
        }

        public static PropertyInfo FindProperty(Type type, string name)
        {
            TypeHelper th = GetHelperForType(type);
            return th.FindProperty(name);
        }
    }

    internal class Document
    {
        public int Length;
        public Document Parent;
        public int Digested;
    }

    public enum Types
    {
        Double = 1,
        String = 2,
        Object = 3,
        Array = 4,
        Binary = 5,
        Undefined = 6,
        ObjectId = 7,
        Boolean = 8,
        DateTime = 9,
        Null = 10,
        Regex = 11,
        Reference = 12,
        Code = 13,
        Symbol = 14,
        ScopedCode = 15,
        Int32 = 16,
        Timestamp = 17,
        Int64 = 18,
        Float = 19,
        Element = 20,
        Set = 21,
        BehaviorElement = 22,
        PropertiesElement = 23,
        ParsElement = 24,
        ParElement = 25,
        NodeElement = 26,
        AttachmentsElement = 27,
        AttachmentElement = 28
    }

    public class BsonSerializer
    {
        public static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);        

        private static readonly IDictionary<Type, Types> _typeMap = new Dictionary<Type, Types>
                                                                            {
                                                                                {typeof (int), Types.Int32},
                                                                                {typeof (long), Types.Int64},
                                                                                {typeof (bool), Types.Boolean},
                                                                                {typeof (string), Types.String},
                                                                                {typeof (double), Types.Double},
                                                                                {typeof (Guid), Types.Binary},
                                                                                {typeof (Regex), Types.Regex},
                                                                                {typeof (DateTime), Types.DateTime},
                                                                                {typeof (float), Types.Float},
                                                                                {typeof (byte[]), Types.Binary}
                                                                            };

        private readonly BinaryWriter _writer;
        private Document _current;

        public static void Serialize<T>(BinaryWriter writer, T document)
        {
            var type = document.GetType();
            if (type.IsValueType || typeof(IEnumerable).IsAssignableFrom(type))
            {
                throw new BsonException("Root type must be an non-enumerable object");
            }

            {
                BsonSerializer s = new BsonSerializer(writer);
                s.WriteDocument(document);
            }
        }

        private BsonSerializer(BinaryWriter writer)
        {
            _writer = writer;
        }

        public static BsonSerializer CreateSerialize(BinaryWriter writer)
        {
            BsonSerializer s = new BsonSerializer(writer);
            return s;
        }

        public void WriteStartDocument()
        {
            this.NewDocument();
        }

        public void WriteEndDocument()
        {
            this.EndDocument(true);
        }

        public void WriteComment(string name)
        {
            //
        }

        public void WriteStartElement(string name)
        {
            Types type = Types.Element;
            if (name == "behavior")
            {
                type = Types.BehaviorElement;
            }
            else if (name == "properties")
            {
                type = Types.PropertiesElement;
            }
            else if (name == "pars")
            {
                type = Types.ParsElement;
            }
            else if (name == "par")
            {
                type = Types.ParElement;
            }
            else if (name == "attachments")
            {
                type = Types.AttachmentsElement;
            }
            else if (name == "attachment")
            {
                type = Types.AttachmentElement;
            }
            else if (name == "node")
            {
                type = Types.NodeElement;
            }
            else
            {
                System.Diagnostics.Debug.Assert(false);
            }

            this.Write(type);

            if (type == Types.Element)
            {
                this.WriteName(name);
            }

            this.NewDocument();
        }

        public void WriteEndElement()
        {
            EndDocument(true);
        }


        public void WriteAttributeString(string name, string value)
        {
            this.SerializeMember(name, value);
        }

        public void WriteAttribute(string name, object value)
        {
            this.SerializeMember(name, value);
        }

        public void WriteString(string value)
        {
            this.Write(value);
        }

        public void Write(object value)
        {
            this.Write(value);
        }


        private void NewDocument()
        {
            var old = _current;
            _current = new Document { Parent = old, Length = (int)_writer.BaseStream.Position, Digested = sizeof(int) };
            _writer.Write((int)0); // length placeholder
        }
        private void EndDocument(bool includeEeo)
        {
            var old = _current;
            if (includeEeo)
            {
                Written(1);
                _writer.Write((byte)0);
            }

            _writer.Seek(_current.Length, SeekOrigin.Begin);
            System.Diagnostics.Debug.Assert(_current.Digested <= int.MaxValue);
            _writer.Write((int)_current.Digested); // override the document length placeholder
            _writer.Seek(0, SeekOrigin.End); // back to the end
            _current = _current.Parent;
            if (_current != null)
            {
                Written(old.Digested);
            }
        }

        private void Written(int length)
        {
            _current.Digested += length;
        }

        private void WriteDocument(object document)
        {
            NewDocument();
            WriteObject(document);
            EndDocument(true);
        }

        private void WriteObject(object document)
        {
            var typeHelper = TypeHelper.GetHelperForType(document.GetType());
            foreach (var property in typeHelper.GetProperties())
            {
                var value = property.GetValue(document, null);
                SerializeMember(property.Name, value);
            }
        }

        private void SerializeMember(string name, object value)
        {
            if (value == null)
            {
                Write(Types.Null);
                WriteName(name);
                return;
            }

            var type = value.GetType();
            if (type.IsEnum)
            {
                type = Enum.GetUnderlyingType(type);
            }

            Types storageType;
            if (!_typeMap.TryGetValue(type, out storageType))
            {
                // this isn't a simple type;
                Write(name, value);
                return;
            }

            Write(storageType);
            WriteName(name);
            switch (storageType)
            {
                case Types.Int32:
                    Written(4);
                    _writer.Write((int)value);
                    return;
                case Types.Int64:
                    Written(8);
                    _writer.Write((long)value);
                    return;
                case Types.String:
                    Write((string)value);
                    return;
                case Types.Double:
                    Written(8);
                    if (value is float)
                    {
                        _writer.Write(Convert.ToDouble((float)value));
                    }
                    else
                    {
                        _writer.Write((double)value);
                    }

                    return;
                case Types.Boolean:
                    Written(1);
                    _writer.Write((bool)value ? (byte)1 : (byte)0);
                    return;
                case Types.DateTime:
                    Written(8);
                    _writer.Write((long)((DateTime)value).Subtract(Epoch).TotalMilliseconds);
                    return;
                case Types.Binary:
                    WriteBinnary(value);
                    return;
                case Types.Regex:
                    Write((Regex)value);
                    break;
            }
        }

        private void Write(string name, object value)
        {
            if (value is IDictionary)
            {
                Write(Types.Object);
                WriteName(name);
                NewDocument();
                Write((IDictionary)value);
                EndDocument(true);
            }
            else if (value is IList)
            {
                Write(Types.Array);
                WriteName(name);
                IList list = value as IList;
                Type listType = list.GetType();
                Type elementType = typeof(object);
                if (listType.IsArray)
                {
                    elementType = listType.GetElementType();
                }

                if (listType.IsGenericType)
                {
                    elementType = listType.GetGenericArguments()[0];
                }

                string nativeTypeName = Plugin.GetNativeTypeName(elementType.Name);
                WriteName(nativeTypeName);

                NewDocument();
                Write((IList)value);
                EndDocument(true);
            }
            else if (value is IEnumerable)
            {
                System.Diagnostics.Debug.Assert(false);
            }
            else
            {
                Write(Types.Object);
                WriteName(name);
                WriteDocument(value); // Write manages new/end document
            }
        }

        private void Write(IList list)
        {
            var index = 0;
            foreach (var value in list)
            {
                SerializeMember((index++).ToString(), value);
            }
        }

        private void Write(IDictionary dictionary)
        {
            foreach (var key in dictionary.Keys)
            {
                SerializeMember((string)key, dictionary[key]);
            }
        }

        private void WriteBinnary(object value)
        {
            System.Diagnostics.Debug.Assert(false);

            if (value is byte[])
            {
                var bytes = (byte[])value;
                var length = bytes.Length;
                _writer.Write(length + 4);
                _writer.Write((byte)2);
                _writer.Write(length);
                _writer.Write(bytes);
                Written(9 + length);
            }
            else if (value is Guid)
            {
                var guid = (Guid)value;
                var bytes = guid.ToByteArray();
                _writer.Write(bytes.Length);
                _writer.Write((byte)3);
                _writer.Write(bytes);
                Written(5 + bytes.Length);
            }
        }

        private void Write(Types type)
        {
            _writer.Write((byte)type);
            Written(1);
        }

        private void WriteName(string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name);

            System.Diagnostics.Debug.Assert(bytes.Length + 1 <= ushort.MaxValue);
            ushort len = (ushort)(bytes.Length + 1);
            _writer.Write(len);
            _writer.Write(bytes);
            _writer.Write((byte)0);
            Written(bytes.Length + 1 + 2);
        }

        private void Write(string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            System.Diagnostics.Debug.Assert(bytes.Length + 1 <= ushort.MaxValue);
            ushort len = (ushort)(bytes.Length + 1);
            _writer.Write(len);
            _writer.Write(bytes);
            _writer.Write((byte)0);
            Written(bytes.Length + 1 + 2); // stringLength + null byte
        }

        private void Write(Regex regex)
        {
            System.Diagnostics.Debug.Assert(false);

            WriteName(regex.ToString());

            var options = string.Empty;
            if ((regex.Options & RegexOptions.ECMAScript) == RegexOptions.ECMAScript)
            {
                options = string.Concat(options, 'e');
            }

            if ((regex.Options & RegexOptions.IgnoreCase) == RegexOptions.IgnoreCase)
            {
                options = string.Concat(options, 'i');
            }

            if ((regex.Options & RegexOptions.CultureInvariant) == RegexOptions.CultureInvariant)
            {
                options = string.Concat(options, 'l');
            }

            if ((regex.Options & RegexOptions.Multiline) == RegexOptions.Multiline)
            {
                options = string.Concat(options, 'm');
            }

            if ((regex.Options & RegexOptions.Singleline) == RegexOptions.Singleline)
            {
                options = string.Concat(options, 's');
            }

            options = string.Concat(options, 'u'); // all .net regex are unicode regex, therefore:
            if ((regex.Options & RegexOptions.IgnorePatternWhitespace) == RegexOptions.IgnorePatternWhitespace)
            {
                options = string.Concat(options, 'w');
            }

            if ((regex.Options & RegexOptions.ExplicitCapture) == RegexOptions.ExplicitCapture)
            {
                options = string.Concat(options, 'x');
            }

            WriteName(options);
        }
    }
}