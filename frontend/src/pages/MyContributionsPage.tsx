import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Chip, EmptyState, List, ListRow, Pagination } from 'acordanza-design-system';
import * as contributionsApi from '../api/contributions';
import type { ContributionKind } from '../api/contributions';
import { queryKeys } from '../api/queryKeys';
import { errorMessage } from '../api/errorMessage';
import { validationStatusLabels } from '../api/types';
import type { Contribution } from '../api/types';
import { formatDate } from '../format';

const PAGE_SIZE = 10;

// Tab keys are the API's kind strings, so the active tab indexes the endpoint directly.
const tabLabels: Record<ContributionKind, string> = {
  routes: 'Vías',
  areas: 'Sectores',
  'first-ascensionists': 'Aperturistas',
};

export function MyContributionsPage() {
  const [activeTab, setActiveTab] = useState<ContributionKind>('routes');
  // Per tab, so switching away and back returns to the page you were on.
  const [pages, setPages] = useState<Record<ContributionKind, number>>({
    routes: 1,
    areas: 1,
    'first-ascensionists': 1,
  });

  // One query, keyed on the *active* tab: a tab that was never opened is never part of a
  // query key, so its endpoint is never called.
  const { data, error } = useQuery({
    queryKey: queryKeys.contributions(activeTab, pages[activeTab]),
    queryFn: () => contributionsApi.listMyContributions(activeTab, pages[activeTab], PAGE_SIZE),
    placeholderData: keepPreviousData,
  });

  const items = data?.items ?? [];

  return (
    <>
      <h1>Contribuciones</h1>

      {error ? (
        <p className="error-text">{errorMessage(error, 'No se pudieron cargar tus contribuciones')}</p>
      ) : (
        <>
          <div className="tab-bar">
            {(Object.keys(tabLabels) as ContributionKind[]).map((tab) => (
              <button
                key={tab}
                type="button"
                className={`tab-item${activeTab === tab ? ' active' : ''}`}
                onClick={() => setActiveTab(tab)}
              >
                {tabLabels[tab]}
              </button>
            ))}
          </div>

          <section className="settings-section">
            {items.length === 0 ? (
              <EmptyState>Todavía no has creado nada aquí</EmptyState>
            ) : (
              <>
                <List>
                  {items.map((c) => (
                    <ListRow
                      key={c.id}
                      title={c.name}
                      subtitle={<ContributionSubtitle contribution={c} />}
                      trailing={
                      <Chip status={c.validationStatus}>{validationStatusLabels[c.validationStatus]}</Chip>
                    } />
                  ))}
                </List>
                <Pagination
                  page={pages[activeTab]}
                  totalCount={data?.totalCount ?? 0}
                  pageSize={PAGE_SIZE}
                  onPageChange={(page) => setPages((prev) => ({ ...prev, [activeTab]: page }))}
                />
              </>
            )}
          </section>
        </>
      )}
    </>
  );
}

function ContributionSubtitle({ contribution }: { contribution: Contribution }) {
  const detail = contribution.detail ? `${contribution.detail}` : '';
  const created = `Creado el ${formatDate(contribution.createdAt)}`
  const decided = contribution.decidedByName && contribution.decidedAt ? ` ·  Validado por ${contribution.decidedByName} el ${formatDate(contribution.decidedAt)}` : '';
  const rejected = contribution.validationStatus === 'rejected' && contribution.rejectionReason ? ` · Motivo: ${contribution.rejectionReason}` : '';
  const validation = `${created}${decided}${rejected}`
  return (
    <>
      {detail || validation}
      {detail && <span className="user-row-meta">{validation}</span>}
    </>
  );
}
