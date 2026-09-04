// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.IO;

namespace ManagedCodeGen
{
    // Return borrowed line spans so the overwhelmingly common instruction lines need no strings.
    internal sealed class DisassemblyReader : IDisposable
    {
        private readonly StreamReader _reader;
        private char[] _buffer = ArrayPool<char>.Shared.Rent(64 * 1024);
        private int _start;
        private int _end;
        private bool _skipLF;
        private bool _eof;

        public DisassemblyReader(string path)
        {
            _reader = new StreamReader(path);
        }

        public bool ReadLine(out ReadOnlySpan<char> line)
        {
            int scanned = 0;
            while (true)
            {
                ReadOnlySpan<char> remaining = _buffer.AsSpan(_start, _end - _start);
                if (_skipLF && !remaining.IsEmpty)
                {
                    _skipLF = false;
                    if (remaining[0] == '\n')
                    {
                        _start++;
                        remaining = remaining.Slice(1);
                    }
                }

                int newline = remaining.Slice(scanned).IndexOfAny('\r', '\n');
                if (newline >= 0)
                {
                    newline += scanned;
                    line = remaining.Slice(0, newline);
                    _skipLF = remaining[newline] == '\r';
                    _start += newline + 1;
                    return true;
                }

                if (_eof)
                {
                    line = remaining;
                    _start = _end;
                    return !line.IsEmpty;
                }

                scanned = remaining.Length;
                if (remaining.Length == _buffer.Length)
                {
                    char[] larger = ArrayPool<char>.Shared.Rent(checked(_buffer.Length * 2));
                    remaining.CopyTo(larger);
                    ArrayPool<char>.Shared.Return(_buffer);
                    _buffer = larger;
                }
                else
                {
                    remaining.CopyTo(_buffer);
                }

                _start = 0;
                _end = scanned;
                int read = _reader.Read(_buffer.AsSpan(_end));
                _end += read;
                _eof = read == 0;
            }
        }

        public void Dispose()
        {
            _reader.Dispose();
            ArrayPool<char>.Shared.Return(_buffer);
        }
    }
}
