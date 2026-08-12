import { useContext } from 'react';
import { PendingTasksContext } from '../context/pending-tasks-context';

export function usePendingTasks() {
  const ctx = useContext(PendingTasksContext);
  if (!ctx) throw new Error('usePendingTasks must be used within PendingTasksProvider');
  return ctx;
}
