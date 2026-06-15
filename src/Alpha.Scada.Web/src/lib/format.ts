import type { Tag, Unit } from "../api/types";

const preferredProcessSteps = [
  { label: "Fuel Feed", key: "fuel.wood_chip_feed_kg_h", unit: "kg/h" },
  { label: "Gasifier", key: "gasifier.reactor_temp_c", unit: "degC" },
  { label: "Gas Cleaning", key: "gas_cleaning.filter_dp_mbar", unit: "mbar" },
  { label: "Engine", key: "engine.electrical_output_kw", unit: "kW" },
  { label: "Heat Recovery", key: "heat.thermal_output_kw", unit: "kW" }
];

const preferredKpis = [
  { label: "Electrical Output", key: "engine.electrical_output_kw", tone: "cyan" },
  { label: "Thermal Output", key: "heat.thermal_output_kw", tone: "lime" },
  { label: "Fuel Feed", key: "fuel.wood_chip_feed_kg_h", tone: "blue" },
  { label: "CO Level", key: "safety.co_ppm", tone: "amber" }
] satisfies Array<{ label: string; key: string; tone: OverviewKpi["tone"] }>;

export type OverviewItem = {
  key: string;
  label: string;
  unit: string;
};

export type OverviewKpi = OverviewItem & {
  tone: "cyan" | "lime" | "blue" | "amber";
};

export function buildProcessSteps(tags: Tag[]): OverviewItem[] {
  const preferred = preferredProcessSteps
    .filter(step => tags.some(tag => tag.tagKey === step.key));
  if (preferred.length >= 2) {
    return preferred;
  }

  const seenSubsystems = new Set<string>();
  return tags
    .filter(tag => {
      if (seenSubsystems.has(tag.subsystem)) return false;
      seenSubsystems.add(tag.subsystem);
      return true;
    })
    .slice(0, 5)
    .map(tag => ({
      key: tag.tagKey,
      label: tag.subsystem || tag.name,
      unit: tag.engineeringUnit
    }));
}

export function buildOverviewKpis(tags: Tag[]): OverviewKpi[] {
  const selected = preferredKpis
    .map(metric => {
      const tag = tags.find(item => item.tagKey === metric.key);
      return tag ? { metric, tag } : undefined;
    })
    .filter((item): item is { metric: typeof preferredKpis[number]; tag: Tag } => item !== undefined);
  const selectedKeys = new Set(selected.map(item => item.tag.tagKey));

  for (const tag of tags) {
    if (selected.length >= 4) break;
    if (!selectedKeys.has(tag.tagKey)) {
      selected.push({
        metric: { label: tag.name, key: tag.tagKey, tone: "cyan" as const },
        tag
      });
      selectedKeys.add(tag.tagKey);
    }
  }

  return selected.slice(0, 4).map(({ metric, tag }) => ({
    key: tag.tagKey,
    label: metric.label,
    unit: tag.engineeringUnit,
    tone: metric.tone
  }));
}

export function tagValue(tags: Tag[], key: string, unit: string) {
  const tag = tags.find(item => item.tagKey === key);
  return tag?.value === null || tag === undefined ? "--" : `${format(tag.value)} ${unit}`;
}

export function unitName(units: Unit[], unitId: string) {
  return units.find(unit => unit.id === unitId)?.name ?? "Unit";
}

export function format(value: number | null) {
  if (value === null) return "--";
  return Number(value).toLocaleString(undefined, { maximumFractionDigits: 1 });
}

export function groupBy<T>(items: T[], selector: (item: T) => string) {
  return items.reduce<Record<string, T[]>>((groups, item) => {
    const key = selector(item);
    groups[key] ??= [];
    groups[key].push(item);
    return groups;
  }, {});
}
