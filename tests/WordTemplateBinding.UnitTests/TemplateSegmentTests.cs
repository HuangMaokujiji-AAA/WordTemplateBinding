using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WordTemplateBinding.Core.Models;
using WordTemplateBinding.Infrastructure.OpenXml.Segments;

namespace WordTemplateBinding.UnitTests;

#pragma warning disable CS1591

public sealed class TemplateSegmentTests
{
    [Fact]
    public async Task ScanAsync_UnmarkedDocument_CreatesVirtualFullDocument()
    {
        byte[] bytes = CreateDocument(new Paragraph(new Run(new Text("普通正文"))));
        OpenXmlTemplateSegmentScanner scanner = new();

        await using MemoryStream stream = new(bytes);
        TemplateSegmentScanResult result =
            await scanner.ScanAsync(stream, CancellationToken.None);

        TemplateSegmentDefinition segment = Assert.Single(result.Segments);
        Assert.Equal("full-document", segment.SegmentKey);
        Assert.Equal("ROOT", segment.SegmentType);
        Assert.Equal("VIRTUAL", segment.AnchorType);
        Assert.Contains(0, segment.MainDocumentParagraphIndexes);
    }

    [Fact]
    public async Task ScanAsync_NestedContentControls_AssignsParentAndStableNames()
    {
        SdtBlock child = Segment("child", "子片段", "子正文");
        SdtBlock parent = new(
            new SdtProperties(
                new SdtAlias { Val = "父片段" },
                new Tag { Val = "wtb:segment:parent" }),
            new SdtContentBlock(
                new Paragraph(new Run(new Text("父正文"))),
                child));
        byte[] bytes = CreateDocument(parent);
        OpenXmlTemplateSegmentScanner scanner = new();

        await using MemoryStream stream = new(bytes);
        TemplateSegmentScanResult result =
            await scanner.ScanAsync(stream, CancellationToken.None);

        Assert.Equal(2, result.Segments.Count);
        TemplateSegmentDefinition childResult =
            Assert.Single(result.Segments.Where(item => item.SegmentKey == "child"));
        Assert.Equal("parent", childResult.ParentSegmentKey);
        Assert.Equal("子片段", childResult.SegmentName);
        Assert.True(childResult.Depth > 0);
    }

    [Fact]
    public async Task ScanAsync_DuplicateTags_ReturnsDiagnosticAndFallbackSegment()
    {
        byte[] bytes = CreateDocument(
            Segment("same", "一", "正文一"),
            Segment("same", "二", "正文二"));
        OpenXmlTemplateSegmentScanner scanner = new();

        await using MemoryStream stream = new(bytes);
        TemplateSegmentScanResult result =
            await scanner.ScanAsync(stream, CancellationToken.None);

        Assert.Equal("full-document", Assert.Single(result.Segments).SegmentKey);
        Assert.Contains(result.Diagnostics,
            item => item.Code == "SEGMENT_TAG_DUPLICATED" && item.Level == "ERROR");
    }

    [Fact]
    public async Task PreviewRenderer_KeepsOnlySelectedSegmentBody()
    {
        byte[] bytes = CreateDocument(
            Segment("first", "第一", "保留正文"),
            Segment("second", "第二", "移除正文"));
        string sourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");
        await File.WriteAllBytesAsync(sourcePath, bytes);
        try
        {
            TemplateSegmentRecord segment = new()
            {
                Id = 1,
                TemplateVersionId = 1,
                SegmentKey = "first",
                SegmentName = "第一",
                SegmentType = "SECTION",
                AnchorType = "CONTENT_CONTROL",
                StartAnchorJson =
                    """{"partKind":"MainDocument","partKey":"/word/document.xml","locatorType":"CONTENT_CONTROL","tag":"wtb:segment:first"}""",
                DocumentOrderStart = 0,
                DocumentOrderEnd = 1,
                SegmentStatus = "READY",
                PreviewStatus = "NOT_CREATED",
                SortNo = 0,
                RowVersion = 0,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            OpenXmlSegmentPreviewRenderer renderer = new();

            await using Stream preview =
                await renderer.RenderAsync(sourcePath, segment, CancellationToken.None);
            using WordprocessingDocument document =
                WordprocessingDocument.Open(preview, false);
            string text = document.MainDocumentPart!.Document.Body!.InnerText;
            Assert.Contains("保留正文", text, StringComparison.Ordinal);
            Assert.DoesNotContain("移除正文", text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task SegmentEditor_InsertAndRemoveBoundary_PreservesAllBodyContent()
    {
        byte[] bytes = CreateDocument(
            new Paragraph(new Run(new Text("第一段"))),
            new Paragraph(new Run(new Text("第二段"))),
            new Paragraph(new Run(new Text("第三段"))));
        string sourcePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.docx");
        await File.WriteAllBytesAsync(sourcePath, bytes);
        try
        {
            OpenXmlTemplateSegmentEditor editor = new();
            IReadOnlyList<TemplateOutlineBlock> outline =
                await editor.ReadOutlineAsync(sourcePath);
            Assert.Equal(3, outline.Count);
            Assert.Equal("body/0", outline[0].BlockId);

            await using Stream inserted = await editor.InsertBoundaryAsync(
                sourcePath,
                new InsertTemplateSegmentBoundaryRequest
                {
                    SegmentKey = "first-two",
                    SegmentName = "前两段",
                    StartBlockId = "body/0",
                    EndBlockId = "body/1",
                    ExpectedContentHash = "unused-by-editor",
                });
            string insertedPath = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.docx");
            await File.WriteAllBytesAsync(
                insertedPath,
                ((MemoryStream)inserted).ToArray());
            try
            {
                using (WordprocessingDocument document =
                       WordprocessingDocument.Open(insertedPath, false))
                {
                    Body body = document.MainDocumentPart!.Document.Body!;
                    SdtBlock boundary = Assert.Single(body.Elements<SdtBlock>());
                    Assert.Equal(
                        "wtb:segment:first-two",
                        boundary.SdtProperties!.GetFirstChild<Tag>()!.Val!.Value);
                    Assert.Equal("第一段第二段第三段", body.InnerText);
                }

                await using Stream removed = await editor.RemoveBoundaryAsync(
                    insertedPath,
                    "first-two");
                using WordprocessingDocument removedDocument =
                    WordprocessingDocument.Open(removed, false);
                Body removedBody =
                    removedDocument.MainDocumentPart!.Document.Body!;
                Assert.Empty(removedBody.Descendants<SdtBlock>());
                Assert.Equal(
                    new[] { "第一段", "第二段", "第三段" },
                    removedBody.Elements<Paragraph>().Select(item => item.InnerText));
            }
            finally
            {
                File.Delete(insertedPath);
            }
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task SegmentEditor_InsertBoundaries_WritesOneCopyWithAllRanges()
    {
        byte[] bytes = CreateDocument(
            new Paragraph(new Run(new Text("第一段"))),
            new Paragraph(new Run(new Text("第二段"))),
            new Paragraph(new Run(new Text("第三段"))),
            new Paragraph(new Run(new Text("第四段"))));
        string sourcePath = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.docx");
        await File.WriteAllBytesAsync(sourcePath, bytes);
        try
        {
            OpenXmlTemplateSegmentEditor editor = new();
            await using Stream edited = await editor.InsertBoundariesAsync(
                sourcePath,
                new[]
                {
                    new InsertTemplateSegmentBoundaryRequest
                    {
                        SegmentKey = "first",
                        SegmentName = "第一部分",
                        StartBlockId = "body/0",
                        EndBlockId = "body/1",
                        ExpectedContentHash = "unused-by-editor",
                    },
                    new InsertTemplateSegmentBoundaryRequest
                    {
                        SegmentKey = "second",
                        SegmentName = "第二部分",
                        StartBlockId = "body/2",
                        EndBlockId = "body/3",
                        ExpectedContentHash = "unused-by-editor",
                    },
                });

            using WordprocessingDocument document =
                WordprocessingDocument.Open(edited, false);
            Body body = document.MainDocumentPart!.Document.Body!;
            Assert.Equal(
                new[] { "wtb:segment:first", "wtb:segment:second" },
                body.Elements<SdtBlock>()
                    .Select(block => block.SdtProperties!
                        .GetFirstChild<Tag>()!.Val!.Value));
            Assert.Equal("第一段第二段第三段第四段", body.InnerText);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task SegmentEditor_InsertBoundaries_RejectsOverlappingRanges()
    {
        byte[] bytes = CreateDocument(
            new Paragraph(new Run(new Text("第一段"))),
            new Paragraph(new Run(new Text("第二段"))),
            new Paragraph(new Run(new Text("第三段"))));
        string sourcePath = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.docx");
        await File.WriteAllBytesAsync(sourcePath, bytes);
        try
        {
            OpenXmlTemplateSegmentEditor editor = new();
            InvalidDataException exception = await Assert.ThrowsAsync<
                InvalidDataException>(async () =>
            {
                await using Stream _ = await editor.InsertBoundariesAsync(
                    sourcePath,
                    new[]
                    {
                        new InsertTemplateSegmentBoundaryRequest
                        {
                            SegmentKey = "first",
                            SegmentName = "第一部分",
                            StartBlockId = "body/0",
                            EndBlockId = "body/1",
                            ExpectedContentHash = "unused-by-editor",
                        },
                        new InsertTemplateSegmentBoundaryRequest
                        {
                            SegmentKey = "second",
                            SegmentName = "第二部分",
                            StartBlockId = "body/1",
                            EndBlockId = "body/2",
                            ExpectedContentHash = "unused-by-editor",
                        },
                    });
            });

            Assert.Contains("不能重叠", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    private static SdtBlock Segment(string key, string alias, string text) => new(
        new SdtProperties(
            new SdtAlias { Val = alias },
            new Tag { Val = $"wtb:segment:{key}" }),
        new SdtContentBlock(new Paragraph(new Run(new Text(text)))));

    private static byte[] CreateDocument(params OpenXmlElement[] bodyChildren)
    {
        using MemoryStream stream = new();
        using (WordprocessingDocument document =
               WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            MainDocumentPart main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(bodyChildren));
            main.Document.Save();
        }

        return stream.ToArray();
    }
}

#pragma warning restore CS1591
