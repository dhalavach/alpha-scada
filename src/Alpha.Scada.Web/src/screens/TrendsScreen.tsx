import type { HistoryPoint, Tag } from "../api/types";
import Badge from "../components/Badge";
import Kpi from "../components/Kpi";
import TrendChart from "../components/TrendChart";
import { format } from "../lib/format";

type TrendsScreenProps = {
  tags: Tag[];
  selectedTag?: Tag;
  selectedTagId: string;
  trendWindow: number;
  history: HistoryPoint[];
  historyStatus: string;
  setSelectedTagId: (value: string) => void;
  setTrendWindow: (value: number) => void;
  refresh: () => Promise<void>;
};

export default function TrendsScreen({
  tags,
  selectedTag,
  selectedTagId,
  trendWindow,
  history,
  historyStatus,
  setSelectedTagId,
  setTrendWindow,
  refresh
}: TrendsScreenProps) {
  return (
    <section className="screenStack">
      <section className="panel">
        <div className="panelHeader">
          <div>
            <p className="eyebrow">Historian</p>
            <h2>Trend Explorer</h2>
          </div>
          <div className="filterRow">
            <select value={selectedTagId} onChange={event => setSelectedTagId(event.target.value)}>
              {tags.map(tag => <option key={tag.tagId} value={tag.tagId}>{tag.name}</option>)}
            </select>
            {[15, 30, 60, 240].map(minutes => (
              <button
                className={trendWindow === minutes ? "segmented active" : "segmented"}
                key={minutes}
                onClick={() => setTrendWindow(minutes)}
              >
                {minutes}m
              </button>
            ))}
            <button className="iconButton" onClick={refresh}>Refresh</button>
          </div>
        </div>
        <TrendChart points={history} unit={selectedTag?.engineeringUnit ?? ""} />
      </section>

      <section className="kpiGrid three">
        <Kpi label="Selected Tag" value={selectedTag ? selectedTag.name : "--"} tone="cyan" />
        <Kpi label="History Points" value={String(history.length)} tone="lime" />
        <Kpi label="History Status" value={historyStatus} tone="blue" />
      </section>

      <section className="panel">
        <div className="panelHeader">
          <div>
            <p className="eyebrow">Samples</p>
            <h2>Recent History</h2>
          </div>
        </div>
        <div className="dataTable compact">
          <div className="tableHead">
            <span>Time</span>
            <span>Value</span>
            <span>Quality</span>
          </div>
          {history.slice(-12).reverse().map(point => (
            <div className="tableRow" key={`${point.timestampUtc}-${point.value}`}>
              <span>{new Date(point.timestampUtc).toLocaleTimeString()}</span>
              <span className="mono">{format(point.value)} {selectedTag?.engineeringUnit}</span>
              <span><Badge value={point.quality} tone={point.quality === "good" ? "good" : "warn"} /></span>
            </div>
          ))}
        </div>
      </section>
    </section>
  );
}
