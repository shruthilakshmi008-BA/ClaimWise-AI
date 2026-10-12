import type { AuditEntry } from '../../api/types';
import { dateTime, humanize } from '../../lib/format';
import { Card } from '../common';

export function AuditTimeline({ entries }: { entries: AuditEntry[] }) {
  return (
    <Card title="Audit history" id="audit">
      <ol className="timeline">
        {entries.map((e) => (
          <li key={e.id} className={e.action.startsWith('AiSummary') ? 'ai' : undefined}>
            <div className="timeline-head">
              <strong>{humanize(e.action)}</strong>
              <span className="muted small">{dateTime(e.timestamp)} · {e.actor}</span>
            </div>
            <div>{e.details}</div>
          </li>
        ))}
      </ol>
    </Card>
  );
}
