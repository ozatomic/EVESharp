using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Utilities.Zlib;
using PythonTypes.Compression;
using PythonTypes.Types.Database;
using PythonTypes.Types.Primitives;

namespace PythonTypes.Marshal
{
    public class Unmarshal
    {
        public static PyDataType ReadFromByteArray(byte[] data, bool expectHeader = true)
        {
            MemoryStream stream = new MemoryStream(data);

            return ReadFromStream(stream, expectHeader);
        }

        public static PyDataType ReadFromStream(Stream stream, bool expectHeader = true)
        {
            Unmarshal processor = new Unmarshal(stream);

            return processor.Process();
        }

        private Stream mStream;
        private BinaryReader mReader;
        private PyDataType[] mSavedList;
        private int mCurrentSavedIndex;
        private int[] mSavedElementsMap;

        public Unmarshal(Stream stream)
        {
            this.mStream = stream;
        }

        /// <summary>
        /// Processes a python type from the current position of mStream
        /// </summary>
        /// <param name="expectHeader">Whether the unmarshaler should check for a Marshal header or not</param>
        /// <returns></returns>
        /// <exception cref="InvalidDataException">If any error was found during the unmarshal process</exception>
        public PyDataType Process(bool expectHeader = true)
        {
            byte header;

            if (expectHeader)
            {
                // check the header and ensure we use the correct stream to read from it
                header = (byte) this.mStream.ReadByte();

                if (header == Specification.ZlibHeader)
                {
                    this.mStream.Seek(-1, SeekOrigin.Current);
                    this.mStream = new ZInputStream(this.mStream);
                    header = (byte) this.mStream.ReadByte();
                }

                if (header != Specification.MarshalHeader)
                    throw new InvalidDataException($"Expected Marshal header, but got {header}");

                // create the reader
                this.mReader = new BinaryReader(this.mStream);
                // read the save list information
                int saveCount = this.mReader.ReadInt32();

                // check if there are elements in the save list and parse the list-map first
                if (saveCount > 0)
                {
                    // compressed streams cannot be seek'd so ensure that the stream is decompressed first
                    if (this.mStream is ZInputStream)
                    {
                        MemoryStream newStream = new MemoryStream();
                        this.mStream.CopyTo(newStream);
                        this.mStream = newStream;
                        this.mReader = new BinaryReader(this.mStream);
                    }

                    // reserve space for the savelist map and the actual elements in the save list
                    this.mSavedElementsMap = new int[saveCount];
                    this.mSavedList = new PyDataType[saveCount];

                    long currentPosition = this.mStream.Position;
                    // read at the end of the stream to get the correct index to element mapping
                    this.mStream.Seek(-saveCount * 4, SeekOrigin.End);

                    for (int i = 0; i < saveCount; i++)
                        this.mSavedElementsMap[i] = this.mReader.ReadInt32();

                    // go back to where the stream was after reading the amount of saved elements
                    this.mStream.Seek(currentPosition, SeekOrigin.Begin);
                }
            }

            // read the type's opcode from the stream
            header = this.mReader.ReadByte();
            Opcode opcode = (Opcode) (header & Specification.OpcodeMask);
            bool save = (header & Specification.SaveMask) == Specification.SaveMask;

            PyDataType data;

            // perform decode
            switch (opcode)
            {
                case Opcode.IntegerLongLong:
                case Opcode.IntegerSignedShort:
                case Opcode.IntegerByte:
                case Opcode.IntegerMinusOne:
                case Opcode.IntegerOne:
                case Opcode.IntegerZero:
                case Opcode.IntegerLong:
                case Opcode.IntegerVar:
                    data = ProcessInteger(opcode);
                    break;

                case Opcode.None:
                    data = ProcessNone(opcode);
                    break;

                case Opcode.Buffer:
                    data = ProcessBuffer(opcode);
                    break;

                case Opcode.BoolFalse:
                case Opcode.BoolTrue:
                    data = ProcessBool(opcode);
                    break;

                case Opcode.Real:
                case Opcode.RealZero:
                    data = ProcessDecimal(opcode);
                    break;

                case Opcode.Token:
                    data = ProcessToken(opcode);
                    break;

                case Opcode.StringEmpty:
                case Opcode.StringChar:
                case Opcode.StringShort:
                case Opcode.StringTable:
                case Opcode.StringLong:
                case Opcode.WStringEmpty:
                case Opcode.WStringUCS2:
                case Opcode.WStringUCS2Char:
                case Opcode.WStringUTF8:
                    data = ProcessString(opcode);
                    break;
                case Opcode.Tuple:
                case Opcode.TupleOne:
                case Opcode.TupleTwo:
                case Opcode.TupleEmpty:
                    data = ProcessTuple(opcode);
                    break;

                case Opcode.List:
                case Opcode.ListOne:
                case Opcode.ListEmpty:
                    data = ProcessList(opcode);
                    break;

                case Opcode.Dictionary:
                    data = ProcessDictionary(opcode);
                    break;

                case Opcode.PackedRow:
                    data = ProcessPackedRow(opcode);
                    break;

                case Opcode.ObjectData:
                    data = ProcessObjectData(opcode);
                    break;

                case Opcode.ObjectType1:
                case Opcode.ObjectType2:
                    data = ProcessObject(opcode);
                    break;

                case Opcode.SavedStreamElement:
                    data = this.mSavedList[
                        this.mSavedElementsMap[this.mReader.ReadSizeEx() - 1] - 1
                    ];
                    break;

                case Opcode.SubStruct:
                    data = ProcessSubStruct(opcode);
                    break;

                case Opcode.SubStream:
                    data = ProcessSubStream(opcode);
                    break;

                case Opcode.ChecksumedStream:
                    data = ProcessChecksumedStream(opcode);
                    break;

                default:
                    throw new InvalidDataException($"Unknown python type for opcode {opcode:X}");
            }

            // check if the element has to be saved
            if (save == true)
                this.mSavedList[this.mSavedElementsMap[this.mCurrentSavedIndex++] - 1] = data;

            return data;
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessInteger"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.IntegerLongLong"/>
        /// <seealso cref="Opcode.IntegerLong"/>
        /// <seealso cref="Opcode.IntegerSignedShort"/>
        /// <seealso cref="Opcode.IntegerByte"/>
        /// <seealso cref="Opcode.IntegerVar"/>
        /// <seealso cref="Opcode.IntegerOne"/>
        /// <seealso cref="Opcode.IntegerZero"/>
        /// <seealso cref="Opcode.IntegerMinusOne"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was detected in the data</exception>
        private PyDataType ProcessInteger(Opcode opcode)
        {
            switch (opcode)
            {
                case Opcode.IntegerLongLong:
                    return new PyInteger(mReader.ReadInt64());
                case Opcode.IntegerLong:
                    return new PyInteger(mReader.ReadInt32());
                case Opcode.IntegerSignedShort:
                    return new PyInteger(mReader.ReadInt16());
                case Opcode.IntegerByte:
                    return new PyInteger(mReader.ReadByte());
                case Opcode.IntegerOne:
                    return new PyInteger(1);
                case Opcode.IntegerZero:
                    return new PyInteger(0);
                case Opcode.IntegerMinusOne:
                    return new PyInteger(-1);
                case Opcode.IntegerVar:
                {
                    // read the size
                    uint length = mReader.ReadSizeEx();
                    // emergency fallback, for longer integers read it as a PyBuffer
                    if (length > 8)
                        return new PyBuffer(mReader.ReadBytes((int) length));
                    if (length == 1)
                        return new PyInteger(mReader.ReadByte());
                    if (length == 2)
                        return new PyInteger(mReader.ReadInt16());
                    if (length == 4)
                        return new PyInteger(mReader.ReadInt32());
                    if (length == 8)
                        return new PyInteger(mReader.ReadInt64());

                    throw new InvalidDataException($"IntegerVar of odd size ({length})");
                }
                default:
                    throw new InvalidDataException($"Trying to parse a {opcode} as Integer");
            }
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessNone"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.None"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessNone(Opcode opcode)
        {
            if (opcode != Opcode.None)
                throw new InvalidDataException($"Trying to parse a {opcode} as None");

            return new PyNone();
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessBuffer"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.Buffer"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessBuffer(Opcode opcode)
        {
            if (opcode != Opcode.Buffer)
                throw new InvalidDataException($"Trying to parse a {opcode} as Buffer");

            uint length = this.mReader.ReadSizeEx();

            return new PyBuffer(this.mReader.ReadBytes((int) length));
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessBool"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.BoolTrue"/>
        /// <seealso cref="Opcode.BoolFalse"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessBool(Opcode opcode)
        {
            switch (opcode)
            {
                case Opcode.BoolTrue:
                    return new PyBool(true);
                case Opcode.BoolFalse:
                    return new PyBool(false);
                default:
                    throw new InvalidDataException($"Trying to parse a {opcode} as Boolean");
            }
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessDecimal"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.RealZero"/>
        /// <seealso cref="Opcode.Real"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessDecimal(Opcode opcode)
        {
            switch (opcode)
            {
                case Opcode.RealZero:
                    return new PyDecimal(0.0);
                case Opcode.Real:
                    return new PyDecimal(this.mReader.ReadDouble());
                default:
                    throw new InvalidDataException($"Trying to parse a {opcode} as Decimal");
            }
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessToken"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.Token"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessToken(Opcode opcode)
        {
            if (opcode != Opcode.Token)
                throw new InvalidDataException($"Trying to parse a {opcode} as Token");

            return new PyToken(
                Encoding.ASCII.GetString(
                    this.mReader.ReadBytes(this.mReader.ReadByte())
                )
            );
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessString"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.StringEmpty"/>
        /// <seealso cref="Opcode.WStringEmpty"/>
        /// <seealso cref="Opcode.StringChar"/>
        /// <seealso cref="Opcode.WStringUCS2Char"/>
        /// <seealso cref="Opcode.StringTable"/>
        /// <seealso cref="Opcode.WStringUCS2"/>
        /// <seealso cref="Opcode.StringShort"/>
        /// <seealso cref="Opcode.StringLong"/>
        /// <seealso cref="Opcode.WStringUTF8"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessString(Opcode opcode)
        {
            switch (opcode)
            {
                case Opcode.StringEmpty:
                    return new PyString("");
                case Opcode.StringChar:
                    return new PyString(new string(this.mReader.ReadChar(), 1));
                case Opcode.StringShort:
                    return new PyString(
                        Encoding.ASCII.GetString(
                            this.mReader.ReadBytes(this.mReader.ReadByte())
                        )
                    );
                case Opcode.StringTable:
                    return new PyString(
                        StringTableUtils.Entries[this.mReader.ReadByte() - 1]
                    );
                case Opcode.StringLong:
                    return new PyString(
                        Encoding.ASCII.GetString(
                            this.mReader.ReadBytes((int) this.mReader.ReadSizeEx())
                        )
                    );
                case Opcode.WStringEmpty:
                    return new PyString("");
                case Opcode.WStringUCS2:
                    return new PyString(
                        Encoding.Unicode.GetString(
                            this.mReader.ReadBytes((int) this.mReader.ReadSizeEx())
                        )
                    );
                case Opcode.WStringUCS2Char:
                    return new PyString(
                        Encoding.Unicode.GetString(
                            this.mReader.ReadBytes(2)
                        )
                    );
                case Opcode.WStringUTF8:
                    return new PyString(
                        Encoding.UTF8.GetString(
                            this.mReader.ReadBytes((int) this.mReader.ReadSizeEx())
                        )
                    );

                default:
                    throw new InvalidDataException($"Trying to parse a {opcode} as String");
            }
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessTuple"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.TupleEmpty"/>
        /// <seealso cref="Opcode.TupleOne"/>
        /// <seealso cref="Opcode.TupleTwo"/>
        /// <seealso cref="Opcode.Tuple"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessTuple(Opcode opcode)
        {
            switch (opcode)
            {
                case Opcode.Tuple:
                {
                    uint count = this.mReader.ReadSizeEx();
                    PyTuple tuple = new PyTuple((int) count);

                    for (int i = 0; i < count; i++)
                        tuple[i] = this.Process(false);

                    return tuple;
                }
                case Opcode.TupleOne:
                    return new PyTuple(new PyDataType[]
                    {
                        this.Process(false)
                    });
                case Opcode.TupleTwo:
                    return new PyTuple(new PyDataType[]
                    {
                        this.Process(false),
                        this.Process(false)
                    });
                case Opcode.TupleEmpty:
                    return new PyTuple(0);
                default:
                    throw new InvalidDataException($"Trying to parse a {opcode} as Tuple");
            }
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessList"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.ListEmpty"/>
        /// <seealso cref="Opcode.List"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessList(Opcode opcode)
        {
            switch (opcode)
            {
                case Opcode.ListEmpty:
                    return new PyList();
                case Opcode.ListOne:
                    return new PyList(new PyDataType[]
                    {
                        this.Process(false)
                    });
                case Opcode.List:
                {
                    uint count = this.mReader.ReadSizeEx();
                    PyList list = new PyList((int) count);

                    for (int i = 0; i < count; i++)
                        list.Add(this.Process(false));

                    return list;
                }
                default:
                    throw new InvalidDataException($"Trying to parse a {opcode} as List");
            }
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessDictionary"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.Dictionary"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessDictionary(Opcode opcode)
        {
            if (opcode != Opcode.Dictionary)
                throw new InvalidDataException($"Trying to parse a {opcode} as Dictionary");

            PyDictionary dictionary = new PyDictionary();
            uint size = this.mReader.ReadSizeEx();

            while (size-- > 0)
            {
                PyDataType value = this.Process(false);
                PyDataType key = this.Process(false);

                if (key is PyString == false)
                    throw new InvalidDataException($"Expected String as Dictionary key, but gor {key.GetType()}");

                dictionary[key as PyString] = value;
            }

            return dictionary;
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessPackedRow"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.PackedRow"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessPackedRow(Opcode opcode)
        {
            if (opcode != Opcode.PackedRow)
                throw new InvalidDataException($"Trying to parse a {opcode} as PackedRow");

            DBRowDescriptor descriptor = this.Process(false);
            PyDataType[] data = new PyDataType[descriptor.Columns.Count];

            MemoryStream decompressedStream = ZeroCompressionUtils.LoadZeroCompressed(this.mReader);
            BinaryReader decompressedReader = new BinaryReader(decompressedStream);

            // sort columns by the bit size
            IEnumerable<DBRowDescriptor.Column> enumerator =
                descriptor.Columns.OrderByDescending(c => Utils.GetTypeBits(c.Type));

            int bitOffset = 8;
            byte buffer = 0;
            int index = 0;

            foreach (DBRowDescriptor.Column column in enumerator)
                switch (column.Type)
                {
                    case FieldType.I8:
                    case FieldType.UI8:
                    case FieldType.CY:
                    case FieldType.FileTime:
                        data[index++] = new PyInteger(decompressedReader.ReadInt64());
                        break;
                    case FieldType.I4:
                    case FieldType.UI4:
                        data[index++] = new PyInteger(decompressedReader.ReadInt32());
                        break;
                    case FieldType.I2:
                    case FieldType.UI2:
                        data[index++] = new PyInteger(decompressedReader.ReadInt16());
                        break;
                    case FieldType.I1:
                    case FieldType.UI1:
                        data[index++] = new PyInteger(decompressedReader.ReadByte());
                        break;
                    case FieldType.R8:
                        data[index++] = new PyDecimal(decompressedReader.ReadDouble());
                        break;
                    case FieldType.R4:
                        data[index++] = new PyDecimal(decompressedReader.ReadSingle());
                        break;
                    case FieldType.Bool:
                        // read a byte from the buffer if needed
                        if (bitOffset == 8)
                        {
                            buffer = decompressedReader.ReadByte();
                            bitOffset = 0;
                        }

                        data[index++] = new PyBool(((buffer >> bitOffset++) & 0x01) == 0x01);
                        break;
                    case FieldType.Bytes:
                    case FieldType.WStr:
                    case FieldType.Str:
                        data[index++] = this.Process(false);
                        break;
                }

            return new PyPackedRow(descriptor, data);
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessObjectData"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.ObjectData"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessObjectData(Opcode opcode)
        {
            if (opcode != Opcode.ObjectData)
                throw new InvalidDataException($"Trying to parse a {opcode} as ObjectData");

            return new PyObjectData(
                this.Process(false) as PyString, this.Process(false)
            );
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessObject"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.ObjectType1"/>
        /// <seealso cref="Opcode.ObjectType2"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessObject(Opcode opcode)
        {
            if (opcode != Opcode.ObjectType1 && opcode != Opcode.ObjectType2)
                throw new InvalidDataException($"Trying to parse a {opcode} as ObjectEx");

            PyTuple header = this.Process(false) as PyTuple;
            PyList list = new PyList();
            PyDictionary dict = new PyDictionary();

            while (this.mReader.PeekChar() != Marshal.PackedTerminator)
                list.Add(this.Process(false));

            // ignore packed terminator
            this.mReader.ReadByte();

            while (this.mReader.PeekChar() != Marshal.PackedTerminator)
            {
                PyString key = this.Process(false) as PyString;
                PyDataType value = this.Process(false);

                dict[key] = value;
            }

            // ignore packed terminator
            this.mReader.ReadByte();

            return new PyObject(opcode == Opcode.ObjectType2, header, list, dict);
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessSubStruct"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.SubStruct"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessSubStruct(Opcode opcode)
        {
            if (opcode != Opcode.SubStruct)
                throw new InvalidDataException($"Trying to parse a {opcode} as SubStruct");

            return new PySubStruct(
                this.Process(false)
            );
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessSubStream"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.SubStream"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessSubStream(Opcode opcode)
        {
            if (opcode != Opcode.SubStream)
                throw new InvalidDataException($"Trying to parse a {opcode} as SubStream");

            uint length = this.mReader.ReadSizeEx();
            PyDataType result = null;

            // on compressed streams it's impossible to do boundary-checks, so ensure that is taken into account
            if (this.mReader.BaseStream is ZInputStream)
            {
                byte[] buffer = new byte[length];

                this.mStream.Read(buffer, 0, buffer.Length);

                result = ReadFromByteArray(buffer);
            }
            else
            {
                long start = this.mStream.Position;

                // this substream has it's own savelist etc, the best way is to create a new unmarshaler using the same
                // stream so the data can be properly read without much issue
                result = ReadFromStream(this.mStream);

                if ((start + length) != this.mStream.Position)
                    throw new InvalidDataException($"Read past the boundaries of the PySubStream");
            }

            return new PySubStream(result);
        }

        /// <summary>
        /// <seealso cref="Marshal.ProcessChecksumedStream"/>
        /// 
        /// Opcodes supported:
        /// <seealso cref="Opcode.ChecksumedStream"/>
        /// </summary>
        /// <param name="opcode">Type of object to parse</param>
        /// <returns>The decoded python type</returns>
        /// <exception cref="InvalidDataException">If any error was found in the data</exception>
        private PyDataType ProcessChecksumedStream(Opcode opcode)
        {
            if (opcode != Opcode.ChecksumedStream)
                throw new InvalidDataException($"Trying to parse a {opcode} as ChecksumedStream");

            uint checksum = this.mReader.ReadUInt32();

            return new PyChecksumedStream(
                this.Process(false)
            );
        }
    }
}