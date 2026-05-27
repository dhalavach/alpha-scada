import type { HistoryPoint } from "../api/types";
import { format } from "../lib/format";

type TrendChartProps = {
  points: HistoryPoint[];
  unit: string;
};

export default function TrendChart({ points, unit }: TrendChartProps) {
  if (points.length === 0) {
    return <div className="chartEmpty">No history samples in this window</div>;
  }

  const width = 900;
  const height = 280;
  const padding = 36;
  const values = points.map(point => point.value);
  const min = Math.min(...values);
  const max = Math.max(...values);
  const span = Math.max(max - min, 1);
  const coordinates = points.map((point, index) => {
    const x = padding + (index / Math.max(points.length - 1, 1)) * (width - padding * 2);
    const y = height - padding - ((point.value - min) / span) * (height - padding * 2);
    return `${x},${y}`;
  }).join(" ");

  return (
    <div className="trendChart">
      <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label="Tag history trend">
        <line x1={padding} y1={padding} x2={padding} y2={height - padding} />
        <line x1={padding} y1={height - padding} x2={width - padding} y2={height - padding} />
        <polyline points={coordinates} />
        <text x={padding} y={24}>{format(max)} {unit}</text>
        <text x={padding} y={height - 10}>{format(min)} {unit}</text>
      </svg>
    </div>
  );
}
