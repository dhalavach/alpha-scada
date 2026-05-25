create table if not exists units (
    id text primary key,
    name text not null,
    model text not null,
    site_name text not null,
    created_at timestamptz not null default now()
);

create table if not exists tags (
    key text primary key,
    unit_id text not null references units(id),
    subsystem text not null,
    name text not null,
    engineering_unit text not null,
    alarm_low double precision,
    alarm_high double precision
);

create table if not exists tag_current (
    tag_key text primary key references tags(key),
    value_double double precision not null,
    quality text not null,
    timestamp_utc timestamptz not null
);

create table if not exists telemetry_samples (
    tag_key text not null references tags(key),
    timestamp_utc timestamptz not null,
    value_double double precision not null,
    quality text not null,
    primary key (tag_key, timestamp_utc)
);

create table if not exists alarm_events (
    id text primary key,
    tag_key text not null references tags(key),
    severity text not null,
    message text not null,
    raised_at_utc timestamptz not null,
    cleared_at_utc timestamptz
);
