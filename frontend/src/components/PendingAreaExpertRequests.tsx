import * as areaExpertsApi from '../api/areaExperts';
import type { AreaExpert } from '../api/types';
import { PendingRequestsList } from './PendingRequestsList';

interface Props {
  items: AreaExpert[];
  onChange: () => void;
}

export function PendingAreaExpertRequests({ items, onChange }: Props) {
  return (
    <PendingRequestsList
      items={items}
      subjectLabel="Sector"
      subjectName={(e) => e.areaName}
      onApprove={areaExpertsApi.approveAreaExpert}
      onReject={areaExpertsApi.rejectAreaExpert}
      onChange={onChange}
    />
  );
}
