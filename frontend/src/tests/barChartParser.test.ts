import { describe, it, expect } from "vitest";
import { parseAllSeries } from "../features/docx/chart-recognition/parsers/chartSeriesParser";
import { parseCategories } from "../features/docx/chart-recognition/parsers/chartCategoryParser";
import { parseChartLegend } from "../features/docx/chart-recognition/parsers/chartLegendParser";
import { parseXmlString } from "../features/docx/ooxml/xmlUtils";

function makeBarChartXml(
  barDir: string,
  grouping: string,
  seriesData: Array<{ name: string; values: number[] }>,
  categories: string[]
): string {
  const serXml = seriesData
    .map(
      (s, i) => `
    <c:ser>
      <c:idx val="${i}"/>
      <c:order val="${i}"/>
      <c:tx>
        <c:strRef>
          <c:strCache>
            <c:ptCount val="1"/>
            <c:pt idx="0"><c:v>${s.name}</c:v></c:pt>
          </c:strCache>
        </c:strRef>
      </c:tx>
      <c:spPr>
        <a:solidFill xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <a:srgbClr val="4472C4"/>
        </a:solidFill>
      </c:spPr>
      <c:cat>
        <c:strRef>
          <c:strCache>
            <c:ptCount val="${categories.length}"/>
            ${categories.map((c, j) => `<c:pt idx="${j}"><c:v>${c}</c:v></c:pt>`).join("\n            ")}
          </c:strCache>
        </c:strRef>
      </c:cat>
      <c:val>
        <c:numRef>
          <c:numCache>
            <c:formatCode>General</c:formatCode>
            <c:ptCount val="${s.values.length}"/>
            ${s.values.map((v, j) => `<c:pt idx="${j}"><c:v>${v}</c:v></c:pt>`).join("\n            ")}
          </c:numCache>
        </c:numRef>
      </c:val>
      <c:dLbls>
        <c:showVal val="1"/>
        <c:dLblPos val="outEnd"/>
      </c:dLbls>
    </c:ser>`
    )
    .join("\n");

  return `<?xml version="1.0"?>
<c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
              xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
  <c:chart>
    <c:title>
      <c:tx>
        <c:rich>
          <a:p>
            <a:r><a:t>测试图表标题</a:t></a:r>
          </a:p>
        </c:rich>
      </c:tx>
    </c:title>
    <c:legend>
      <c:legendPos val="b"/>
    </c:legend>
    <c:plotArea>
      <c:barChart>
        <c:barDir val="${barDir}"/>
        <c:grouping val="${grouping}"/>
        ${serXml}
        <c:axId val="1"/>
        <c:axId val="2"/>
      </c:barChart>
      <c:catAx>
        <c:axId val="1"/>
        <c:scaling><c:orientation val="minMax"/></c:scaling>
        <c:delete val="0"/>
      </c:catAx>
      <c:valAx>
        <c:axId val="2"/>
        <c:scaling><c:orientation val="minMax"/></c:scaling>
        <c:delete val="0"/>
      </c:valAx>
    </c:plotArea>
  </c:chart>
</c:chartSpace>`;
}

describe("parseAllSeries", () => {
  it("parses series names from chart XML", () => {
    const xml = makeBarChartXml("col", "clustered", [{ name: "你县", values: [543, 505] }], ["四年级", "八年级"]);
    const doc = parseXmlString(xml);
    const plotArea = doc.getElementsByTagNameNS("http://schemas.openxmlformats.org/drawingml/2006/chart", "plotArea")[0];
    const barChart = plotArea.getElementsByTagNameNS("http://schemas.openxmlformats.org/drawingml/2006/chart", "barChart")[0];
    const series = parseAllSeries(barChart);

    expect(series).toHaveLength(1);
    expect(series[0].name).toBe("你县");
  });

  it("parses series values sorted by idx", () => {
    const xml = makeBarChartXml("col", "clustered", [{ name: "你县", values: [543, 505] }], ["四年级", "八年级"]);
    const doc = parseXmlString(xml);
    const plotArea = doc.getElementsByTagNameNS("http://schemas.openxmlformats.org/drawingml/2006/chart", "plotArea")[0];
    const barChart = plotArea.getElementsByTagNameNS("http://schemas.openxmlformats.org/drawingml/2006/chart", "barChart")[0];
    const series = parseAllSeries(barChart);

    expect(series).toHaveLength(1);
    expect(series[0].values).toEqual([543, 505]);
  });

  it("parses multiple series", () => {
    const xml = makeBarChartXml("col", "clustered", [
      { name: "你县", values: [543, 505] },
      { name: "全省", values: [506, 493] },
    ], ["四年级", "八年级"]);
    const doc = parseXmlString(xml);
    const plotArea = doc.getElementsByTagNameNS("http://schemas.openxmlformats.org/drawingml/2006/chart", "plotArea")[0];
    const barChart = plotArea.getElementsByTagNameNS("http://schemas.openxmlformats.org/drawingml/2006/chart", "barChart")[0];
    const series = parseAllSeries(barChart);

    expect(series).toHaveLength(2);
    expect(series[0].name).toBe("你县");
    expect(series[1].name).toBe("全省");
  });
});

describe("parseCategories", () => {
  it("parses category labels sorted by idx", () => {
    const xml = makeBarChartXml("col", "clustered", [{ name: "你县", values: [543, 505] }], ["四年级", "八年级"]);
    const doc = parseXmlString(xml);
    const plotArea = doc.getElementsByTagNameNS("http://schemas.openxmlformats.org/drawingml/2006/chart", "plotArea")[0];
    const barChart = plotArea.getElementsByTagNameNS("http://schemas.openxmlformats.org/drawingml/2006/chart", "barChart")[0];
    const categories = parseCategories(barChart);

    expect(categories).toHaveLength(2);
    expect(categories[0].value).toBe("四年级");
    expect(categories[1].value).toBe("八年级");
  });

  it("parses cognitive domain categories: 识记, 理解, 应用, 分析, 评价, 创造", () => {
    const domains = ["识记", "理解", "应用", "分析", "评价", "创造"];
    const xml = makeBarChartXml("col", "clustered", [{ name: "测试", values: [1, 2, 3, 4, 5, 6] }], domains);
    const doc = parseXmlString(xml);
    const plotArea = doc.getElementsByTagNameNS("http://schemas.openxmlformats.org/drawingml/2006/chart", "plotArea")[0];
    const barChart = plotArea.getElementsByTagNameNS("http://schemas.openxmlformats.org/drawingml/2006/chart", "barChart")[0];
    const categories = parseCategories(barChart);

    expect(categories).toHaveLength(6);
    expect(categories.map((c) => c.value)).toEqual(domains);
  });
});

describe("parseChartLegend", () => {
  it("parses legend with bottom position", () => {
    const xml = makeBarChartXml("col", "clustered", [{ name: "测试", values: [1] }], ["A"]);
    const doc = parseXmlString(xml);
    const legend = parseChartLegend(doc);

    expect(legend.visible).toBe(true);
    expect(legend.position).toBe("bottom");
  });
});
