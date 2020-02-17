using PythonTypes.Marshal;
using Renci.SshNet.Messages.Transport;

namespace PythonTypes.Types.Primitives
{
    public class PyString : PyDataType
    {
        protected bool Equals(PyString other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((PyString) obj);
        }

        public override int GetHashCode()
        {
            return (Value != null ? Value.GetHashCode() : 0);
        }

        public string Value { get; }
        public int Length { get { return this.Value.Length; } }
        public bool IsStringTableEntry { get; }
        public StringTable.EntryList StringTableEntryIndex { get; }
        
        public PyString(string value) : base(PyObjectType.String)
        {
            this.Value = value;
            this.IsStringTableEntry = false;
        }

        public PyString(StringTable.EntryList entry) : base(PyObjectType.String)
        {
            this.Value = StringTable.Entries[(int) entry];
            this.IsStringTableEntry = true;
            this.StringTableEntryIndex = entry;
        }
        
        public static bool operator ==(PyString obj, string value)
        {
            if (ReferenceEquals(null, obj) == true)
            {
                if (value == null)
                    return true;

                return false;
            }

            return obj.Value == value;
        }

        public static bool operator !=(PyString obj, string value)
        {
            return !(obj == value);
        }

        public static implicit operator string(PyString obj)
        {
            if (obj == null)
                return null;
            
            return obj.Value;
        }

        public static implicit operator PyString(string value)
        {
            return new PyString(value);
        }

        public static implicit operator PyString(char value)
        {
            return new PyString(new string (new char [] { value }));
        }
    }
}