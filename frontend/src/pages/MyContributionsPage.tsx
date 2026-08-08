import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Badge, Chip, EmptyState, List, ListRow } from 'acordanza-design-system';
import * as contributionsApi from '../api/contributions';
import { queryKeys } from '../api/queryKeys';
import { errorMessage } from '../api/errorMessage';
import { validationStatusLabels } from '../api/types';
import type { Contribution } from '../api/types';

type Tab = 'routes' | 'areas' | 'firstAscensionists';

const tabLabels: Record<Tab, string> = {
  routes: 'Vías',
  areas: 'Sectores',
  firstAscensionists: 'Aperturistas',
};

export function MyContributionsPage() {
  const [activeTab, setActiveTab] = useState<Tab>('routes');

  const { data, error } = useQuery({
    queryKey: queryKeys.myContributions(),
    queryFn: contributionsApi.getMyContributions,
  });

  const items = data?.[activeTab] ?? [];

  return (
    <>
      <h1>Contribuciones</h1>

      {error ? (
        <p className="error-text">{errorMessage(error, 'No se pudieron cargar tus contribuciones')}</p>
      ) : (
        <>
          <div className="tab-bar">
            {(['routes', 'areas', 'firstAscensionists'] as const).map((tab) => (
              <button
                key={tab}
                type="button"
                className={`tab-item${activeTab === tab ? ' active' : ''}`}
                onClick={() => setActiveTab(tab)}
              >
                {tabLabels[tab]}
                <Badge count={data?.[tab].length ?? 0} />
              </button>
            ))}
          </div>

          <section className="settings-section">
            {items.length === 0 ? (
              <EmptyState>Todavía no has creado nada aquí</EmptyState>
            ) : (
              <List>
                {items.map((c) => (
                  <ListRow key={c.id} title={c.name} subtitle={<ContributionSubtitle contribution={c} />} trailing={
                    <Chip status={c.validationStatus}>{validationStatusLabels[c.validationStatus]}</Chip>
                  } />
                ))}
              </List>
            )}
          </section>
        </>
      )}
    </>
  );
}

function ContributionSubtitle({ contribution }: { contribution: Contribution }) {
  return (
    <>
      {contribution.detail && `${contribution.detail} · `}
      Creado el {new Date(contribution.createdAt).toLocaleDateString()}
      {contribution.decidedByName && ` · ${contribution.decidedByName}`}
      {contribution.validationStatus === 'rejected' &&
        contribution.rejectionReason &&
        ` · Motivo: ${contribution.rejectionReason}`}
    </>
  );
}
