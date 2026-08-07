import * as firstAscensionistLinksApi from '../api/firstAscensionistLinks';
import type { FirstAscensionistLinkRequestSummary } from '../api/types';
import { PendingRequestsList } from './PendingRequestsList';

interface Props {
  items: FirstAscensionistLinkRequestSummary[];
  onChange: () => void;
}

export function PendingFirstAscensionistLinkRequests({ items, onChange }: Props) {
  return (
    <PendingRequestsList
      items={items}
      subjectLabel="Aperturista"
      subjectName={(r) => r.firstAscensionistName}
      onApprove={firstAscensionistLinksApi.approveFirstAscensionistLink}
      onReject={firstAscensionistLinksApi.rejectFirstAscensionistLink}
      onChange={onChange}
    />
  );
}
