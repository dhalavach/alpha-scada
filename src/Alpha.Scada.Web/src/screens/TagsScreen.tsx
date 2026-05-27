import type { Tag } from "../api/types";
import Badge from "../components/Badge";
import Kpi from "../components/Kpi";
import { format } from "../lib/format";

type TagsScreenProps = {
  tags: Tag[];
  allTags: Tag[];
  tagSearch: string;
  subsystemFilter: string;
  subsystems: string[];
  setTagSearch: (value: string) => void;
  setSubsystemFilter: (value: string) => void;
};

export default function TagsScreen({
  tags,
  allTags,
  tagSearch,
  subsystemFilter,
  subsystems,
  setTagSearch,
  setSubsystemFilter
}: TagsScreenProps) {
  const goodQuality = allTags.filter(tag => tag.quality === "good").length;
  return (
    <section className="screenStack">
      <section className="kpiGrid three">
        <Kpi label="Visible Tags" value={String(tags.length)} tone="cyan" />
        <Kpi label="Good Quality" value={`${goodQuality}/${allTags.length}`} tone="lime" />
        <Kpi label="Subsystems" value={String(subsystems.length)} tone="blue" />
      </section>

      <section className="panel">
        <div className="panelHeader">
          <div>
            <p className="eyebrow">Telemetry</p>
            <h2>Live Tag Browser</h2>
          </div>
          <div className="filterRow">
            <input value={tagSearch} onChange={event => setTagSearch(event.target.value)} placeholder="Search tags" />
            <select value={subsystemFilter} onChange={event => setSubsystemFilter(event.target.value)}>
              <option value="all">All subsystems</option>
              {subsystems.map(subsystem => <option key={subsystem} value={subsystem}>{subsystem}</option>)}
            </select>
          </div>
        </div>
        <div className="dataTable">
          <div className="tableHead">
            <span>Tag</span>
            <span>Subsystem</span>
            <span>Value</span>
            <span>Quality</span>
            <span>Timestamp</span>
          </div>
          {tags.map(tag => (
            <div className="tableRow" key={tag.tagId}>
              <span><strong>{tag.name}</strong><small>{tag.tagKey}</small></span>
              <span>{tag.subsystem}</span>
              <span className="mono">{format(tag.value)} {tag.engineeringUnit}</span>
              <span><Badge value={tag.quality} tone={tag.quality === "good" ? "good" : "warn"} /></span>
              <span>{new Date(tag.timestampUtc).toLocaleTimeString()}</span>
            </div>
          ))}
        </div>
      </section>
    </section>
  );
}
