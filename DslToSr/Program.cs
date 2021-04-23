using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace DslToSr
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Usage: DslToSr.exe source dest");
            using (var dslFs = File.OpenRead(args[0]))
            using (ZipArchive dsl = new ZipArchive(dslFs, ZipArchiveMode.Read))
            {
                if (File.Exists(args[1]))
                    File.Delete(args[1]);
                using (var srFs = new FileStream(args[1], FileMode.CreateNew))
                using (ZipArchive sr = new ZipArchive(srFs, ZipArchiveMode.Create))
                {
                    var header = new StreamReader(dsl.GetEntry("header").Open()).ReadToEnd()
                        .Split("\n")
                        .Select(a =>
                        {
                            var x = a.Split("=", 2, StringSplitOptions.RemoveEmptyEntries);
                            if (x.Length == 2)
                                return new
                                {
                                    Key = x[0].Trim(),
                                    Value = x[1].Trim()
                                };
                            return null;
                        })
                        .Where(a => a != null)
                        .ToDictionary(a => a.Key)
                        ;

                    var blocks = Convert.ToInt64(header["total blocks"].Value);
                    var channels = Convert.ToInt32(header["total probes"].Value);

                    int unitsize = 1;
                    var metadata = @$"
[global]
sigrok version=0.5.2

[device 1]
capturefile=logic-1
total probes={channels}
samplerate={header["samplerate"].Value}
total analog=0
{ string.Join("\r\n", Enumerable.Range(0, channels).Select(a => $"probe{a + 1} = {a}"))}
unitsize={unitsize}
";
                    {
                        var entryMeta = sr.CreateEntry($"metadata");
                        StreamWriter sw = new StreamWriter(entryMeta.Open());
                        sw.Write(metadata);
                        sw.Close();
                    }
                    {
                        var entryMeta = sr.CreateEntry($"version");
                        StreamWriter sw = new StreamWriter(entryMeta.Open());
                        sw.Write("2");
                        sw.Close();
                    }

                    for (int blockNr = 0; blockNr < blocks; blockNr++)
                    {
                        byte[][] buffer = new byte[channels][];
                        for (int j = 0; j < channels; j++)
                        {
                            var entry = dsl.GetEntry($"L-{j}/{blockNr}");
                            var stream = entry.Open();
                            buffer[j] = new byte[entry.Length];
                            stream.Read(buffer[j], 0, (int)entry.Length);
                        }


                        var entryDest = sr.CreateEntry($"logic-1-{blockNr}");
                        var destStream = entryDest.Open();

                        MemoryStream ms = new MemoryStream();
                        StreamWriter swBinary = new StreamWriter(ms);
                        for (int i = 0; i < buffer[0].Length; i++)
                        {
                            for (int b = 0; b < 8; b++)
                            {
                                byte x = 0;
                                for (int j = 0; j < channels; j++)
                                {
                                    x |= (byte)(((buffer[j][i] >> b) & 0x01) << j);
                                }
                                swBinary.Write(x);
                            }
                        }
                        swBinary.Flush();
                        ms.Position = 0;
                        ms.CopyTo(destStream);
                        destStream.Flush();
                        destStream.Close();
                    }
                    srFs.Flush();
                }

            }
        }
    }
}
