using System.IO;
using PythonTypes.Types.Primitives;

namespace PythonTypes.Types.Network
{
    public class PyAddressBroadcast : PyAddress
    {
        public PyString Service { get; }
        public PyList IDsOfInterest { get; }
        public PyString IDType { get; }

        public PyAddressBroadcast(PyList idsOfInterest, PyString idType, PyString service = null) : base(TYPE_BROADCAST)
        {
            this.Service = service;
            this.IDsOfInterest = idsOfInterest;
            this.IDType = idType;
        }

        public static implicit operator PyAddressBroadcast(PyObjectData value)
        {
            if (value.Name != OBJECT_TYPE)
                throw new InvalidDataException($"Expected {OBJECT_TYPE} for PyAddress object, got {value.Name}");

            PyTuple data = value.Arguments as PyTuple;
            PyString type = data[0] as PyString;

            if (type != TYPE_BROADCAST)
                throw new InvalidDataException($"Trying to cast unknown object to PyAddressAny");

            return new PyAddressBroadcast(
                data[2] as PyList,
                data[3] as PyString,
                (data[1] is PyNone) ? null : data[1] as PyString
            );
        }

        public static implicit operator PyDataType(PyAddressBroadcast value)
        {
            return new PyObjectData(
                OBJECT_TYPE,
                new PyTuple(new PyDataType[]
                {
                    value.Type,
                    value.Service,
                    value.IDsOfInterest,
                    value.IDType,
                })
            );
        }
    }
}