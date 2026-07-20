using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowsFormsApp1
{
    /// <summary>
    /// Photoshop 图层 TIFF 的单个图层元数据。
    /// </summary>
    public sealed class PhotoshopTiffLayerInfo
    {
        public string Name { get; set; }
        public Rectangle Bounds { get; set; }
        public int ChannelCount { get; set; }
        public bool IsVisible { get; set; }
    }

    /// <summary>
    /// Photoshop TIFF 图层解析结果。
    /// </summary>
    public sealed class PhotoshopTiffLayerParseResult
    {
        public bool HasPhotoshopLayerData { get; set; }
        public string ErrorMessage { get; set; }
        public List<PhotoshopTiffLayerInfo> Layers { get; } = new List<PhotoshopTiffLayerInfo>();
    }

    /// <summary>
    /// 读取 Photoshop 保存的 TIFF 私有标签 37724，并按图层名称还原普通栅格图层。
    /// </summary>
    public static class PhotoshopTiffLayerParser
    {
        private const ushort ImageSourceDataTag = 37724;
        private const ushort ImageWidthTag = 256;
        private const ushort ImageHeightTag = 257;
        private const string PhotoshopDataHeader = "Adobe Photoshop Document Data Block\0";

        private sealed class LayerChannelRecord
        {
            public short Id { get; set; }
            public int DataLength { get; set; }
            public int DataOffset { get; set; }
        }

        private sealed class LayerRecord
        {
            public string Name { get; set; }
            public Rectangle Bounds { get; set; }
            public bool IsVisible { get; set; }
            public List<LayerChannelRecord> Channels { get; } = new List<LayerChannelRecord>();
        }

        private sealed class LayerDocument
        {
            public int CanvasWidth { get; set; }
            public int CanvasHeight { get; set; }
            public bool LittleEndian { get; set; }
            public byte[] SourceData { get; set; }
            public List<LayerRecord> Layers { get; } = new List<LayerRecord>();
        }

        /// <summary>
        /// 解析 TIFF 中 Photoshop 图层的名称、边界、通道数和可见性。
        /// </summary>
        public static PhotoshopTiffLayerParseResult Parse(string filePath)
        {
            var result = new PhotoshopTiffLayerParseResult();
            try
            {
                LayerDocument document = ReadDocument(filePath);
                result.HasPhotoshopLayerData = true;
                foreach (LayerRecord layer in document.Layers)
                {
                    result.Layers.Add(new PhotoshopTiffLayerInfo
                    {
                        Name = layer.Name,
                        Bounds = layer.Bounds,
                        ChannelCount = layer.Channels.Count,
                        IsVisible = layer.IsVisible
                    });
                }

                if (result.Layers.Count == 0)
                    result.ErrorMessage = "已找到 Photoshop 图层数据，但未找到 Layr 图层块。";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 按图层名称还原单个普通栅格图层，并保留原始画布尺寸、位置和透明度。
        /// </summary>
        public static Bitmap RenderLayer(string filePath, string layerName)
        {
            LayerDocument document = ReadDocument(filePath);
            string normalizedName = NormalizeLayerName(layerName);
            List<LayerRecord> matches = document.Layers
                .Where(layer => string.Equals(NormalizeLayerName(layer.Name), normalizedName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
                throw new InvalidDataException($"未找到图层“{layerName}”。");
            if (matches.Count > 1)
                throw new InvalidDataException($"图层“{layerName}”存在重名，无法确定要使用的图层。");

            return RenderRasterLayer(document, matches[0]);
        }

        /// <summary>
        /// 将 TIFF 中全部可见的普通栅格图层按原始层级合成为模板位图，不依赖图层名称。
        /// </summary>
        public static Bitmap RenderVisibleLayers(string filePath)
        {
            LayerDocument document = ReadDocument(filePath);
            List<LayerRecord> visibleLayers = document.Layers.Where(layer => layer.IsVisible).ToList();
            if (visibleLayers.Count == 0)
                throw new InvalidDataException("模板 TIFF 没有可见图层。");

            var result = new Bitmap(document.CanvasWidth, document.CanvasHeight, PixelFormat.Format32bppArgb);
            result.SetResolution(300, 300);
            try
            {
                using (Graphics graphics = Graphics.FromImage(result))
                {
                    graphics.Clear(Color.Transparent);
                    // Photoshop 图层记录由上至下保存，合成时需从底层向顶层绘制。
                    foreach (LayerRecord layer in visibleLayers.AsEnumerable().Reverse())
                    {
                        using (Bitmap layerBitmap = RenderRasterLayer(document, layer))
                        {
                            graphics.DrawImageUnscaled(layerBitmap, 0, 0);
                        }
                    }
                }

                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 验证 TIFF 是否存在唯一的指定图层，用于套图前预检。
        /// </summary>
        public static string ValidateLayer(string filePath, string layerName)
        {
            try
            {
                using (Bitmap ignored = RenderLayer(filePath, layerName))
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// 验证模板中所有可见栅格图层都能被合成，无需约束模板图层名称。
        /// </summary>
        public static string ValidateVisibleLayers(string filePath)
        {
            try
            {
                using (Bitmap ignored = RenderVisibleLayers(filePath))
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// 读取 TIFF 画布信息及 Photoshop 图层私有标签。
        /// </summary>
        private static LayerDocument ReadDocument(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("找不到 TIFF 文件", filePath);

            using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
            {
                bool littleEndian = ReadTiffByteOrder(reader);
                uint firstIfdOffset = ReadUInt32(reader, littleEndian);
                TiffDocumentData tiffData = ReadTiffDocumentData(reader, firstIfdOffset, littleEndian);
                if (tiffData.SourceData == null)
                    throw new InvalidDataException("该 TIFF 未包含 Photoshop 图层私有标签 37724。");
                if (tiffData.Width <= 0 || tiffData.Height <= 0)
                    throw new InvalidDataException("无法读取 TIFF 画布尺寸。");

                var document = new LayerDocument
                {
                    CanvasWidth = tiffData.Width,
                    CanvasHeight = tiffData.Height,
                    LittleEndian = littleEndian,
                    SourceData = tiffData.SourceData
                };
                ParsePhotoshopData(document);
                return document;
            }
        }

        private sealed class TiffDocumentData
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public byte[] SourceData { get; set; }
        }

        /// <summary>
        /// 遍历首个 IFD，读取画布宽高与 Photoshop ImageSourceData 私有标签。
        /// </summary>
        private static TiffDocumentData ReadTiffDocumentData(BinaryReader reader, uint ifdOffset, bool littleEndian)
        {
            var result = new TiffDocumentData();
            reader.BaseStream.Position = ifdOffset;
            ushort entryCount = ReadUInt16(reader, littleEndian);
            for (int i = 0; i < entryCount; i++)
            {
                long entryStart = reader.BaseStream.Position;
                ushort tag = ReadUInt16(reader, littleEndian);
                ushort type = ReadUInt16(reader, littleEndian);
                uint count = ReadUInt32(reader, littleEndian);
                uint valueOrOffset = ReadUInt32(reader, littleEndian);

                if ((tag == ImageWidthTag || tag == ImageHeightTag) && count > 0)
                {
                    int value = ReadInlineTiffValue(type, valueOrOffset, littleEndian);
                    if (tag == ImageWidthTag)
                        result.Width = value;
                    else
                        result.Height = value;
                }
                else if (tag == ImageSourceDataTag)
                {
                    int valueSize = GetTiffTypeSize(type);
                    long byteCount = (long)count * valueSize;
                    if (byteCount <= 0 || byteCount > int.MaxValue)
                        throw new InvalidDataException("Photoshop 图层数据标签长度无效。");

                    if (byteCount <= 4)
                    {
                        reader.BaseStream.Position = entryStart + 8;
                        result.SourceData = reader.ReadBytes((int)byteCount);
                    }
                    else
                    {
                        reader.BaseStream.Position = valueOrOffset;
                        result.SourceData = reader.ReadBytes((int)byteCount);
                        if (result.SourceData.Length != byteCount)
                            throw new EndOfStreamException("Photoshop 图层数据读取不完整。");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 按 Photoshop TIFF ImageSourceData 结构定位并解析 8BIM/Layr 块。
        /// </summary>
        private static void ParsePhotoshopData(LayerDocument document)
        {
            byte[] sourceData = document.SourceData;
            if (sourceData.Length < PhotoshopDataHeader.Length ||
                Encoding.ASCII.GetString(sourceData, 0, PhotoshopDataHeader.Length) != PhotoshopDataHeader)
            {
                throw new InvalidDataException("标签 37724 不包含有效的 Photoshop Document Data Block 头。");
            }

            for (int blockStart = PhotoshopDataHeader.Length; blockStart + 12 <= sourceData.Length; blockStart++)
            {
                string signature = NormalizeFourCc(ReadFourCc(sourceData, blockStart));
                string blockType = NormalizeFourCc(ReadFourCc(sourceData, blockStart + 4));
                if (signature != "8BIM" || blockType != "Layr")
                    continue;

                uint length = ReadUInt32(sourceData, blockStart + 8, document.LittleEndian);
                int dataStart = blockStart + 12;
                if (length > sourceData.Length - dataStart)
                    throw new InvalidDataException("Photoshop 图层数据长度超出标签范围。");

                ParseLayerBlock(document, dataStart, (int)length);
                return;
            }
        }

        /// <summary>
        /// 解析图层记录及其后续通道压缩数据的偏移位置。
        /// </summary>
        private static void ParseLayerBlock(LayerDocument document, int start, int length)
        {
            byte[] data = document.SourceData;
            int end = checked(start + length);
            if (start + 2 > end)
                throw new InvalidDataException("Layr 图层块长度不足。");

            int offset = start;
            int layerCount = Math.Abs(ReadInt16(data, offset, document.LittleEndian));
            offset += 2;

            for (int index = 0; index < layerCount; index++)
            {
                if (offset + 38 > end)
                    throw new InvalidDataException("图层记录不完整。");

                int top = ReadInt32(data, offset, document.LittleEndian); offset += 4;
                int left = ReadInt32(data, offset, document.LittleEndian); offset += 4;
                int bottom = ReadInt32(data, offset, document.LittleEndian); offset += 4;
                int right = ReadInt32(data, offset, document.LittleEndian); offset += 4;
                ushort channelCount = ReadUInt16(data, offset, document.LittleEndian); offset += 2;

                var layer = new LayerRecord
                {
                    Bounds = Rectangle.FromLTRB(left, top, right, bottom)
                };

                for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
                {
                    if (offset + 6 > end)
                        throw new InvalidDataException("图层通道记录不完整。");

                    short channelId = ReadInt16(data, offset, document.LittleEndian); offset += 2;
                    uint channelLength = ReadUInt32(data, offset, document.LittleEndian); offset += 4;
                    if (channelLength > int.MaxValue)
                        throw new InvalidDataException("图层通道数据过大，当前无法处理。");

                    layer.Channels.Add(new LayerChannelRecord
                    {
                        Id = channelId,
                        DataLength = (int)channelLength
                    });
                }

                offset += 8; // 混合模式签名与键名。
                offset += 2; // 不透明度与裁剪标志。
                if (offset + 6 > end)
                    throw new InvalidDataException("图层显示属性不完整。");

                byte flags = data[offset++];
                offset++; // 填充字节。
                uint extraDataLength = ReadUInt32(data, offset, document.LittleEndian); offset += 4;
                int extraDataEnd = checked(offset + (int)extraDataLength);
                if (extraDataEnd > end)
                    throw new InvalidDataException("图层附加数据超出 Layr 块范围。");

                uint maskLength = ReadUInt32(data, offset, document.LittleEndian); offset += 4 + (int)maskLength;
                uint blendRangeLength = ReadUInt32(data, offset, document.LittleEndian); offset += 4 + (int)blendRangeLength;
                layer.Name = ReadPascalString(data, ref offset, extraDataEnd);
                layer.IsVisible = (flags & 0x02) == 0;
                document.Layers.Add(layer);

                offset = extraDataEnd;
            }

            // 图层记录之后按“图层顺序 -> 通道顺序”连续存放每个通道的压缩像素数据。
            int channelDataOffset = offset;
            foreach (LayerRecord layer in document.Layers)
            {
                foreach (LayerChannelRecord channel in layer.Channels)
                {
                    if (channel.DataLength < 2 || channelDataOffset > end - channel.DataLength)
                        throw new InvalidDataException("图层通道像素数据不完整。");

                    channel.DataOffset = channelDataOffset;
                    channelDataOffset += channel.DataLength;
                }
            }
        }

        /// <summary>
        /// 解码图层 RGB 与 Alpha 通道，并绘制到完整 TIFF 画布对应的位置。
        /// </summary>
        private static Bitmap RenderRasterLayer(LayerDocument document, LayerRecord layer)
        {
            if (layer.Bounds.Width <= 0 || layer.Bounds.Height <= 0)
                throw new InvalidDataException($"图层“{layer.Name}”没有有效的像素范围。");

            int pixelCount = checked(layer.Bounds.Width * layer.Bounds.Height);
            byte[] red = new byte[pixelCount];
            byte[] green = new byte[pixelCount];
            byte[] blue = new byte[pixelCount];
            byte[] alpha = Enumerable.Repeat((byte)255, pixelCount).ToArray();
            bool hasRgbChannel = false;

            foreach (LayerChannelRecord channel in layer.Channels)
            {
                byte[] decoded = DecodeChannel(document, layer.Bounds.Width, layer.Bounds.Height, channel);
                switch (channel.Id)
                {
                    case 0: red = decoded; hasRgbChannel = true; break;
                    case 1: green = decoded; hasRgbChannel = true; break;
                    case 2: blue = decoded; hasRgbChannel = true; break;
                    case -1: alpha = decoded; break;
                }
            }

            if (!hasRgbChannel)
                throw new NotSupportedException($"图层“{layer.Name}”不包含可用的 RGB 栅格通道。");

            int[] pixels = new int[checked(document.CanvasWidth * document.CanvasHeight)];
            for (int y = 0; y < layer.Bounds.Height; y++)
            {
                int canvasY = layer.Bounds.Top + y;
                if (canvasY < 0 || canvasY >= document.CanvasHeight)
                    continue;

                for (int x = 0; x < layer.Bounds.Width; x++)
                {
                    int canvasX = layer.Bounds.Left + x;
                    if (canvasX < 0 || canvasX >= document.CanvasWidth)
                        continue;

                    int layerIndex = y * layer.Bounds.Width + x;
                    pixels[canvasY * document.CanvasWidth + canvasX] =
                        (alpha[layerIndex] << 24) |
                        (red[layerIndex] << 16) |
                        (green[layerIndex] << 8) |
                        blue[layerIndex];
                }
            }

            return CreateBitmap(document.CanvasWidth, document.CanvasHeight, pixels);
        }

        /// <summary>
        /// 支持 Photoshop TIFF 常见的未压缩和 PackBits RLE 图层通道。
        /// </summary>
        private static byte[] DecodeChannel(LayerDocument document, int width, int height, LayerChannelRecord channel)
        {
            byte[] source = document.SourceData;
            int start = channel.DataOffset;
            int end = checked(start + channel.DataLength);
            ushort compression = ReadUInt16(source, start, document.LittleEndian);
            int pixelCount = checked(width * height);

            if (compression == 0)
            {
                if (end - start - 2 < pixelCount)
                    throw new InvalidDataException("未压缩图层通道数据不足。");

                var result = new byte[pixelCount];
                Buffer.BlockCopy(source, start + 2, result, 0, pixelCount);
                return result;
            }

            if (compression != 1)
                throw new NotSupportedException($"暂不支持图层通道压缩方式 {compression}，请在 Photoshop 中将 TIFF 图层压缩设为 RLE 或无压缩。");

            int lengthsOffset = start + 2;
            int encodedOffset = checked(lengthsOffset + height * 2);
            if (encodedOffset > end)
                throw new InvalidDataException("RLE 图层行长度表不完整。");

            var pixels = new byte[pixelCount];
            for (int y = 0; y < height; y++)
            {
                int rowLength = ReadUInt16(source, lengthsOffset + y * 2, document.LittleEndian);
                if (rowLength < 0 || encodedOffset > end - rowLength)
                    throw new InvalidDataException("RLE 图层行数据超出通道范围。");

                DecodePackBitsRow(source, encodedOffset, rowLength, pixels, y * width, width);
                encodedOffset += rowLength;
            }

            return pixels;
        }

        private static void DecodePackBitsRow(byte[] source, int sourceOffset, int sourceLength, byte[] destination, int destinationOffset, int expectedLength)
        {
            int sourceEnd = sourceOffset + sourceLength;
            int output = destinationOffset;
            int outputEnd = destinationOffset + expectedLength;
            while (sourceOffset < sourceEnd && output < outputEnd)
            {
                sbyte control = unchecked((sbyte)source[sourceOffset++]);
                if (control >= 0)
                {
                    int count = control + 1;
                    if (sourceOffset > sourceEnd - count || output > outputEnd - count)
                        throw new InvalidDataException("PackBits 原始数据不完整。");

                    Buffer.BlockCopy(source, sourceOffset, destination, output, count);
                    sourceOffset += count;
                    output += count;
                }
                else if (control != -128)
                {
                    int count = 1 - control;
                    if (sourceOffset >= sourceEnd || output > outputEnd - count)
                        throw new InvalidDataException("PackBits 重复数据不完整。");

                    byte value = source[sourceOffset++];
                    for (int i = 0; i < count; i++)
                        destination[output++] = value;
                }
            }

            if (output != outputEnd)
                throw new InvalidDataException("PackBits 解码后的行宽与图层宽度不一致。");
        }

        private static Bitmap CreateBitmap(int width, int height, int[] pixels)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            bitmap.SetResolution(300, 300);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int byteCount = Math.Abs(data.Stride) * height;
                var buffer = new byte[byteCount];
                for (int y = 0; y < height; y++)
                {
                    int row = data.Stride > 0 ? y * data.Stride : (height - 1 - y) * -data.Stride;
                    for (int x = 0; x < width; x++)
                    {
                        int pixel = pixels[y * width + x];
                        int index = row + x * 4;
                        buffer[index] = (byte)pixel;
                        buffer[index + 1] = (byte)(pixel >> 8);
                        buffer[index + 2] = (byte)(pixel >> 16);
                        buffer[index + 3] = (byte)(pixel >> 24);
                    }
                }

                Marshal.Copy(buffer, 0, data.Scan0, byteCount);
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static string ReadPascalString(byte[] data, ref int offset, int end)
        {
            if (offset >= end)
                return string.Empty;

            int length = data[offset++];
            length = Math.Min(length, Math.Max(0, end - offset));
            string value = Encoding.Default.GetString(data, offset, length);
            offset += length;
            offset = Math.Min(AlignToFourBytes(offset), end);
            return value;
        }

        private static string NormalizeLayerName(string name) => (name ?? string.Empty).Trim();
        private static int ReadInlineTiffValue(ushort type, uint value, bool littleEndian) => type == 3 ? (int)(littleEndian ? value & 0xFFFF : value >> 16) : checked((int)value);
        private static int GetTiffTypeSize(ushort type)
        {
            switch (type)
            {
                case 1: case 2: case 6: case 7: return 1;
                case 3: case 8: return 2;
                case 4: case 9: case 11: return 4;
                case 5: case 10: case 12: return 8;
                default: throw new NotSupportedException($"不支持 TIFF 标签类型: {type}");
            }
        }

        private static bool ReadTiffByteOrder(BinaryReader reader)
        {
            byte first = reader.ReadByte();
            byte second = reader.ReadByte();
            bool littleEndian = first == (byte)'I' && second == (byte)'I';
            bool bigEndian = first == (byte)'M' && second == (byte)'M';
            if (!littleEndian && !bigEndian)
                throw new InvalidDataException("不是有效的 TIFF 字节序标记。");
            if (ReadUInt16(reader, littleEndian) != 42)
                throw new NotSupportedException("当前图层解析器仅支持经典 TIFF，不支持 BigTIFF。");
            return littleEndian;
        }

        private static string ReadFourCc(byte[] data, int offset) => Encoding.ASCII.GetString(data, offset, 4);
        private static string NormalizeFourCc(string value) => value == "MIB8" ? "8BIM" : value == "ryaL" ? "Layr" : value;
        private static int AlignToFourBytes(int value) => (value + 3) & ~3;
        private static ushort ReadUInt16(BinaryReader reader, bool littleEndian) => ReadUInt16(reader.ReadBytes(2), 0, littleEndian);
        private static uint ReadUInt32(BinaryReader reader, bool littleEndian) => ReadUInt32(reader.ReadBytes(4), 0, littleEndian);
        private static short ReadInt16(byte[] data, int offset, bool littleEndian) => unchecked((short)ReadUInt16(data, offset, littleEndian));
        private static int ReadInt32(byte[] data, int offset, bool littleEndian) => unchecked((int)ReadUInt32(data, offset, littleEndian));
        private static ushort ReadUInt16(byte[] data, int offset, bool littleEndian) => littleEndian ? (ushort)(data[offset] | data[offset + 1] << 8) : (ushort)(data[offset] << 8 | data[offset + 1]);
        private static uint ReadUInt32(byte[] data, int offset, bool littleEndian) => littleEndian ? (uint)(data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16 | data[offset + 3] << 24) : (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);
    }
}
