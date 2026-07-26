import { describe, it, expect } from "vitest";
import JSZip from "jszip";
import { parseXmlString } from "../../features/docx/ooxml/xmlUtils";
import { analyzeChartXml } from "../../features/docx/chart-analysis/parsers/chartXmlAnalyzer";
import { toWordChartModel } from "../../features/docx/chart-analysis/render/toWordChartModel";
import type { WordRadarChartModel } from "../../features/docx/chart-recognition/types";

const CHART_NS = `xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"`;

function baseInput(chartXmlString: string, overrides: Partial<Parameters<typeof analyzeChartXml>[0]> = {}) {
  return {
    chartXml: parseXmlString(chartXmlString),
    chartXmlPath: "word/charts/chart1.xml",
    chartId: "chart-1-rId5",
    slotId: "chart-1-rId5",
    relationshipId: "rId5",
    documentOrder: 0,
    marker: "[[DOCX_CHART_SLOT:chart-1-rId5]]",
    widthPx: 560,
    heightPx: 320,
    widthEmu: null,
    heightEmu: null,
    zip: new JSZip(),
    ...overrides,
  };
}

function simpleSer(name: string, catValues: string[], values: Array<number | string>): string {
  return `
    <c:ser>
      <c:idx val="0"/><c:order val="0"/>
      <c:tx><c:strRef><c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>${name}</c:v></c:pt></c:strCache></c:strRef></c:tx>
      <c:cat><c:strRef><c:strCache>
        <c:ptCount val="${catValues.length}"/>
        ${catValues.map((v, i) => `<c:pt idx="${i}"><c:v>${v}</c:v></c:pt>`).join("")}
      </c:strCache></c:strRef></c:cat>
      <c:val><c:numRef><c:numCache>
        <c:ptCount val="${values.length}"/>
        ${values.map((v, i) => (v === "" ? "" : `<c:pt idx="${i}"><c:v>${v}</c:v></c:pt>`)).join("")}
      </c:numCache></c:numRef></c:val>
    </c:ser>`;
}

describe("analyzeChartXml — simple bar chart", () => {
  it("produces a fully structured ParsedWordChart", async () => {
    const xml = `<?xml version="1.0"?>
<c:chartSpace ${CHART_NS}>
  <c:chart>
    <c:title><c:tx><c:rich><a:p><a:r><a:t>学院平均成绩</a:t></a:r></a:p></c:rich></c:tx></c:title>
    <c:plotArea>
      <c:barChart>
        <c:barDir val="col"/>
        <c:grouping val="clustered"/>
        ${simpleSer("2025年", ["计算机学院", "软件学院", "人工智能学院"], [88.5, 90.1, 87.3])}
        <c:axId val="1"/><c:axId val="2"/>
      </c:barChart>
      <c:catAx><c:axId val="1"/><c:crossAx val="2"/></c:catAx>
      <c:valAx><c:axId val="2"/><c:crossAx val="1"/></c:valAx>
    </c:plotArea>
    <c:legend><c:legendPos val="b"/></c:legend>
  </c:chart>
</c:chartSpace>`;

    const parsed = await analyzeChartXml(baseInput(xml));

    expect(parsed.type).toBe("column");
    expect(parsed.title?.plainText).toBe("学院平均成绩");
    expect(parsed.categories).toHaveLength(3);
    expect(parsed.series).toHaveLength(1);
    expect(parsed.series[0].values.points.map((p) => p.value)).toEqual([88.5, 90.1, 87.3]);
    expect(parsed.dataTable.rowCount).toBe(3);
    expect(parsed.dataTable.columnCount).toBe(2); // category + 1 series
    expect(parsed.bindingSchema.slots.some((s) => s.role === "dataset" && s.bindable)).toBe(true);
    expect(parsed.supportedForPreview).toBe(true);
    expect(parsed.supportedForBinding).toBe(true);
    expect(parsed.diagnostics.hasErrors).toBe(false);

    // JSON-safety: no DOM/JSZip/Map/function survives serialization.
    const json = JSON.stringify(parsed);
    const revived = JSON.parse(json);
    expect(revived.type).toBe("column");
  });

  it("keeps sparse idx gaps as isMissing without shifting later values (scenario 2 from spec)", async () => {
    const xml = `<?xml version="1.0"?>
<c:chartSpace ${CHART_NS}>
  <c:chart><c:plotArea>
    <c:barChart>
      <c:barDir val="col"/>
      <c:ser>
        <c:idx val="0"/><c:order val="0"/>
        <c:cat><c:strRef><c:strCache>
          <c:ptCount val="3"/>
          <c:pt idx="0"><c:v>A</c:v></c:pt><c:pt idx="1"><c:v>B</c:v></c:pt><c:pt idx="2"><c:v>C</c:v></c:pt>
        </c:strCache></c:strRef></c:cat>
        <c:val><c:numRef><c:numCache>
          <c:ptCount val="3"/>
          <c:pt idx="0"><c:v>10</c:v></c:pt>
          <c:pt idx="2"><c:v>30</c:v></c:pt>
        </c:numCache></c:numRef></c:val>
      </c:ser>
      <c:axId val="1"/><c:axId val="2"/>
    </c:barChart>
    <c:catAx><c:axId val="1"/><c:crossAx val="2"/></c:catAx>
    <c:valAx><c:axId val="2"/><c:crossAx val="1"/></c:valAx>
  </c:plotArea></c:chart>
</c:chartSpace>`;
    const parsed = await analyzeChartXml(baseInput(xml));
    const points = parsed.series[0].values.points;
    expect(points).toHaveLength(3);
    expect(points[0]).toMatchObject({ index: 0, value: 10, isMissing: false });
    expect(points[1]).toMatchObject({ index: 1, value: null, isMissing: true });
    expect(points[2]).toMatchObject({ index: 2, value: 30, isMissing: false });
  });
});

describe("analyzeChartXml — combo chart with real secondary axis", () => {
  it("assigns primary axis to the bar group and secondary axis to the line group", async () => {
    const xml = `<?xml version="1.0"?>
<c:chartSpace ${CHART_NS}>
  <c:chart><c:plotArea>
    <c:barChart>
      <c:barDir val="col"/>
      <c:grouping val="clustered"/>
      ${simpleSer("销售额", ["一月", "二月", "三月"], [100, 120, 130])}
      <c:axId val="1"/><c:axId val="2"/>
    </c:barChart>
    <c:lineChart>
      <c:grouping val="standard"/>
      ${simpleSer("增长率", ["一月", "二月", "三月"], [0.05, 0.2, 0.08])}
      <c:axId val="3"/><c:axId val="4"/>
    </c:lineChart>
    <c:catAx><c:axId val="1"/><c:crossAx val="2"/></c:catAx>
    <c:valAx><c:axId val="2"/><c:crossAx val="1"/></c:valAx>
    <c:valAx><c:axId val="4"/><c:crossAx val="3"/></c:valAx>
    <c:catAx><c:axId val="3"/><c:crossAx val="4"/><c:delete val="1"/></c:catAx>
  </c:plotArea></c:chart>
</c:chartSpace>`;
    const parsed = await analyzeChartXml(baseInput(xml));

    expect(parsed.type).toBe("combo");
    expect(parsed.plotGroups).toHaveLength(2);
    expect(parsed.plotGroups[0].type).toBe("column");
    expect(parsed.plotGroups[1].type).toBe("line");
    expect(parsed.series).toHaveLength(2);

    const barSeries = parsed.series.find((s) => s.chartType === "column");
    const lineSeries = parsed.series.find((s) => s.chartType === "line");
    expect(barSeries?.axisRole).toBe("primary");
    expect(lineSeries?.axisRole).toBe("secondary");

    const valueAxes = parsed.axes.filter((a) => a.type === "value");
    expect(valueAxes.find((a) => a.id === "2")?.role).toBe("y");
    expect(valueAxes.find((a) => a.id === "4")?.role).toBe("secondary-y");

    expect(parsed.supportedForPreview).toBe(true);
  });
});

describe("analyzeChartXml — scatter chart", () => {
  it("produces xy-pairs data table and x/y binding slots", async () => {
    const xml = `<?xml version="1.0"?>
<c:chartSpace ${CHART_NS}>
  <c:chart><c:plotArea>
    <c:scatterChart>
      <c:scatterStyle val="lineMarker"/>
      <c:ser>
        <c:idx val="0"/><c:order val="0"/>
        <c:tx><c:strRef><c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>实验组</c:v></c:pt></c:strCache></c:strRef></c:tx>
        <c:xVal><c:numRef><c:numCache>
          <c:ptCount val="2"/><c:pt idx="0"><c:v>1</c:v></c:pt><c:pt idx="1"><c:v>2</c:v></c:pt>
        </c:numCache></c:numRef></c:xVal>
        <c:yVal><c:numRef><c:numCache>
          <c:ptCount val="2"/><c:pt idx="0"><c:v>10</c:v></c:pt><c:pt idx="1"><c:v>20</c:v></c:pt>
        </c:numCache></c:numRef></c:yVal>
      </c:ser>
      <c:axId val="1"/><c:axId val="2"/>
    </c:scatterChart>
    <c:valAx><c:axId val="1"/><c:crossAx val="2"/></c:valAx>
    <c:valAx><c:axId val="2"/><c:crossAx val="1"/></c:valAx>
  </c:plotArea></c:chart>
</c:chartSpace>`;
    const parsed = await analyzeChartXml(baseInput(xml));

    expect(parsed.type).toBe("scatter");
    expect(parsed.dataTable.orientation).toBe("xy-pairs");
    expect(parsed.bindingSchema.slots.some((s) => s.role === "x-values")).toBe(true);
    expect(parsed.bindingSchema.slots.some((s) => s.role === "y-values")).toBe(true);
    expect(parsed.supportedForPreview).toBe(true);
  });
});

describe("analyzeChartXml — pie chart", () => {
  it("has no axes and produces category+value binding slots", async () => {
    const xml = `<?xml version="1.0"?>
<c:chartSpace ${CHART_NS}>
  <c:chart><c:plotArea>
    <c:pieChart>
      ${simpleSer("占比", ["A", "B", "C"], [30, 40, 30])}
    </c:pieChart>
  </c:plotArea></c:chart>
</c:chartSpace>`;
    const parsed = await analyzeChartXml(baseInput(xml));
    expect(parsed.type).toBe("pie");
    expect(parsed.axes).toEqual([]);
    expect(parsed.bindingSchema.slots.some((s) => s.role === "categories")).toBe(true);
    expect(parsed.supportedForPreview).toBe(true);
  });
});

describe("analyzeChartXml — radar chart", () => {
  it("parses marker style, range and produces a previewable radar model", async () => {
    const xml = `<?xml version="1.0"?>
<c:chartSpace ${CHART_NS}>
  <c:chart>
    <c:title><c:tx><c:rich><a:p><a:r><a:t>雷达图标题</a:t></a:r></a:p></c:rich></c:tx></c:title>
    <c:plotArea>
      <c:radarChart>
        <c:radarStyle val="marker"/>
        ${simpleSer("能力值", ["速度", "力量", "耐力"], [5, 7, 6])}
        <c:axId val="1"/><c:axId val="2"/>
      </c:radarChart>
      <c:catAx><c:axId val="1"/><c:crossAx val="2"/></c:catAx>
      <c:valAx><c:axId val="2"/><c:scaling><c:min val="0"/><c:max val="10"/></c:scaling><c:crossAx val="1"/></c:valAx>
    </c:plotArea>
  </c:chart>
</c:chartSpace>`;
    const parsed = await analyzeChartXml(baseInput(xml));
    expect(parsed.type).toBe("radar");
    expect(parsed.supportedForParsing).toBe(true);
    expect(parsed.supportedForPreview).toBe(true);
    expect(parsed.supportedForBinding).toBe(true);
    expect(parsed.plotGroups[0].radarStyle).toBe("marker");
    expect(parsed.title?.plainText).toBe("雷达图标题");
    expect(parsed.series).toHaveLength(1);
    expect(parsed.series[0].values.points.map((p) => p.value)).toEqual([5, 7, 6]);

    const model = toWordChartModel(parsed);
    expect(model.type).toBe("radar");
    const radarModel = model as WordRadarChartModel;
    expect(radarModel.radarStyle).toBe("marker");
    expect(radarModel.showMarker).toBe(true);
    expect(radarModel.filled).toBe(false);
    expect(radarModel.min).toBe(0);
    expect(radarModel.max).toBe(10);
  });

  it("falls back unknown style to standard and emits a warning", async () => {
    const xml = `<?xml version="1.0"?>
<c:chartSpace ${CHART_NS}><c:chart><c:plotArea>
  <c:radarChart>
    <c:radarStyle val="custom"/>
    ${simpleSer("能力值", ["A", "B", "C"], [83, 70, 65])}
  </c:radarChart>
</c:plotArea></c:chart></c:chartSpace>`;
    const parsed = await analyzeChartXml(baseInput(xml));
    expect(parsed.plotGroups[0].radarStyle).toBe("standard");
    expect(parsed.diagnostics.items.some((item) => item.code === "radar-unknown-style")).toBe(true);
  });

});

describe("analyzeChartXml — unsupported chart types", () => {
  it("does not crash and returns a structured fallback when plotArea is missing", async () => {
    const xml = `<?xml version="1.0"?><c:chartSpace ${CHART_NS}><c:chart></c:chart></c:chartSpace>`;
    const parsed = await analyzeChartXml(baseInput(xml));
    expect(parsed.type).toBe("unsupported");
    expect(parsed.diagnostics.hasErrors).toBe(true);
    expect(() => JSON.stringify(parsed)).not.toThrow();
  });
});

describe("analyzeChartXml — length mismatch between categories and series", () => {
  it("keeps the longer length and marks missing points instead of truncating", async () => {
    const xml = `<?xml version="1.0"?>
<c:chartSpace ${CHART_NS}>
  <c:chart><c:plotArea>
    <c:barChart>
      <c:barDir val="col"/>
      <c:ser>
        <c:idx val="0"/><c:order val="0"/>
        <c:cat><c:strRef><c:strCache>
          <c:ptCount val="5"/>
          <c:pt idx="0"><c:v>A</c:v></c:pt><c:pt idx="1"><c:v>B</c:v></c:pt><c:pt idx="2"><c:v>C</c:v></c:pt><c:pt idx="3"><c:v>D</c:v></c:pt><c:pt idx="4"><c:v>E</c:v></c:pt>
        </c:strCache></c:strRef></c:cat>
        <c:val><c:numRef><c:numCache>
          <c:ptCount val="4"/>
          <c:pt idx="0"><c:v>1</c:v></c:pt><c:pt idx="1"><c:v>2</c:v></c:pt><c:pt idx="2"><c:v>3</c:v></c:pt><c:pt idx="3"><c:v>4</c:v></c:pt>
        </c:numCache></c:numRef></c:val>
      </c:ser>
      <c:axId val="1"/><c:axId val="2"/>
    </c:barChart>
    <c:catAx><c:axId val="1"/><c:crossAx val="2"/></c:catAx>
    <c:valAx><c:axId val="2"/><c:crossAx val="1"/></c:valAx>
  </c:plotArea></c:chart>
</c:chartSpace>`;
    const parsed = await analyzeChartXml(baseInput(xml));
    expect(parsed.categories).toHaveLength(5);
    expect(parsed.series[0].values.points).toHaveLength(4);
    // Data table row count uses the max of category count and series point count.
    expect(parsed.dataTable.rowCount).toBe(5);
    const lastRow = parsed.dataTable.rows[4];
    expect(lastRow.isMissing[parsed.series[0].key]).toBe(true);
  });
});
