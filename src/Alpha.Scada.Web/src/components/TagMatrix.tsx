import type { Tag } from "../api/types";
import { format } from "../lib/format";

type TagMatrixProps = {
  groupedTags: Record<string, Tag[]>;
};

export default function TagMatrix({ groupedTags }: TagMatrixProps) {
  return (
    <section className="panel tagPanel">
      <div className="panelHeader">
        <div>
          <p className="eyebrow">Telemetry</p>
          <h2>Subsystem Tag Matrix</h2>
        </div>
      </div>
      <div className="tagGrid">
        {Object.entries(groupedTags).map(([subsystem, subsystemTags]) => (
          <article className="subsystem" key={subsystem}>
            <h3>{subsystem}</h3>
            {subsystemTags.map(tag => (
              <div className="tagRow" key={tag.tagId}>
                <span>{tag.name}</span>
                <strong>{tag.value === null ? "--" : `${format(tag.value)} ${tag.engineeringUnit}`}</strong>
              </div>
            ))}
          </article>
        ))}
      </div>
    </section>
  );
}
