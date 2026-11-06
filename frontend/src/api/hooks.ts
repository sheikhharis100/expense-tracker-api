import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api } from './client';
import type {
  Account,
  AnnualSummary,
  BudgetMonth,
  Category,
  CategoryInput,
  Column,
  ExpenseFilter,
  ExpenseInput,
  ExpenseList,
  ExpensePatch,
  ExpenseRow,
  IncomeMonth,
  Layout,
  LayoutColumn,
  LedgerChange,
  Matrix,
  Me,
  MeUpdate,
  MonthSetting,
  MonthSummary,
  Occurrence,
  Receipt,
  ReceiptInput,
  RecurringInput,
  RecurringItem,
  RecurringMonth,
  SalaryComponent,
  SalaryPlan,
  SheetImportResult,
  Ym,
} from './types';
import { toast } from '../components/Toast';

// Every write can move several figures (a single expense changes the summary, budgets, the matrix and the
// year), so after any confirmed write all cached reads refresh from the server, which is authoritative.
export function invalidateFor(qc: QueryClient, _change?: Partial<LedgerChange>) {
  qc.invalidateQueries();
}

export const newRequestId = (): string =>
  typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`;

const reportError = (err: Error) => toast(err.message, 'error');

function useWrite<TVars, TResult>(fn: (v: TVars) => Promise<TResult>, options: { quiet?: boolean } = {}) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onError: options.quiet ? undefined : reportError,
    onSettled: () => invalidateFor(qc),
  });
}

const qs = (params: Record<string, string | number | boolean | undefined | null>) => {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') p.set(k, String(v));
  return p.toString();
};

// ---------- Reads ----------

export const useMe = (enabled = true) => useQuery({ queryKey: ['me'], queryFn: () => api<Me>('/api/me'), enabled, staleTime: 60_000 });

export const useSummary = (ym: Ym) =>
  useQuery({ queryKey: ['summary', ym], queryFn: () => api<MonthSummary>(`/api/summary/${ym}`), placeholderData: (p) => p });

export const useAnnual = (year: number) =>
  useQuery({ queryKey: ['annual', year], queryFn: () => api<AnnualSummary>(`/api/summary/year/${year}`), placeholderData: (p) => p });

export const useExpenses = (ym: Ym, filter: ExpenseFilter) =>
  useQuery({
    queryKey: ['expenses', ym, filter],
    queryFn: () => api<ExpenseList>(`/api/expenses?${qs({ month: ym, ...filter })}`),
    placeholderData: (p) => p,
  });

export const useMatrix = (ym: Ym) =>
  useQuery({ queryKey: ['matrix', ym], queryFn: () => api<Matrix>(`/api/expenses/matrix/${ym}`), placeholderData: (p) => p });

export const useIncome = (ym: Ym) =>
  useQuery({ queryKey: ['income', ym], queryFn: () => api<IncomeMonth>(`/api/income/${ym}`), placeholderData: (p) => p });

export const useSalaryPlans = () => useQuery({ queryKey: ['plans'], queryFn: () => api<SalaryPlan[]>('/api/income/plans') });

export const useBudgets = (ym: Ym) =>
  useQuery({ queryKey: ['budgets', ym], queryFn: () => api<BudgetMonth>(`/api/budgets/${ym}`), placeholderData: (p) => p });

export const useRecurring = (ym: Ym) =>
  useQuery({ queryKey: ['recurring', ym], queryFn: () => api<RecurringMonth>(`/api/recurring/${ym}`), placeholderData: (p) => p });

export const useMonthSettings = (ym: Ym) =>
  useQuery({ queryKey: ['monthSettings', ym], queryFn: () => api<MonthSetting>(`/api/months/${ym}/settings`) });

export const useCategories = () => useQuery({ queryKey: ['categories'], queryFn: () => api<Category[]>('/api/categories') });

export const useAccounts = () => useQuery({ queryKey: ['accounts'], queryFn: () => api<Account[]>('/api/accounts') });

export const useColumns = () => useQuery({ queryKey: ['columns'], queryFn: () => api<Column[]>('/api/columns') });

// ---------- Expenses ----------

export const useCreateExpense = () => useWrite((v: ExpenseInput) => api<ExpenseRow>('/api/expenses', { method: 'POST', body: v }), { quiet: true });

export const usePatchExpense = () =>
  useWrite(({ id, ...patch }: ExpensePatch & { id: number }) => api<ExpenseRow>(`/api/expenses/${id}`, { method: 'PATCH', body: patch }));

export const useUpdateExpense = () =>
  useWrite(({ id, ...input }: ExpenseInput & { id: number }) => api<ExpenseRow>(`/api/expenses/${id}`, { method: 'PUT', body: input }), { quiet: true });

export const useDeleteExpense = () =>
  useWrite((v: { id: number; version?: number }) => api<void>(`/api/expenses/${v.id}${v.version ? `?version=${v.version}` : ''}`, { method: 'DELETE' }));

export const useDuplicateExpense = () =>
  useWrite((v: { id: number; date?: string }) =>
    api<ExpenseRow>(`/api/expenses/${v.id}/duplicate`, { method: 'POST', body: { clientRequestId: newRequestId(), date: v.date } }),
  );

export const useRefund = () =>
  useWrite(
    (v: { id: number; clientRequestId: string; date: string; amount: number; description: string }) =>
      api<ExpenseRow>(`/api/expenses/${v.id}/refunds`, { method: 'POST', body: v }),
    { quiet: true },
  );

export const useSetNote = () => useWrite((v: { date: string; text: string }) => api<void>('/api/expenses/notes', { method: 'PUT', body: v }));

// ---------- Columns & layouts ----------

export const useSaveColumn = () =>
  useWrite(
    ({ id, ...input }: { id?: number | null; name: string; type: string; options: string[]; formula?: string | null; decimals: number; version?: number }) =>
      id ? api<Column>(`/api/columns/${id}`, { method: 'PUT', body: input }) : api<Column>('/api/columns', { method: 'POST', body: input }),
    { quiet: true },
  );

export const useRemoveColumn = () =>
  useWrite((v: { id: number; mode?: 'archive' | 'delete' }) => api<void>(`/api/columns/${v.id}${v.mode ? `?mode=${v.mode}` : ''}`, { method: 'DELETE' }), {
    quiet: true,
  });

export const useRestoreColumn = () => useWrite((id: number) => api<void>(`/api/columns/${id}/restore`, { method: 'POST' }));

export const checkFormula = (formula: string, columnId?: number | null) =>
  api<{ ok: boolean; error: string | null; display: string | null }>('/api/columns/check', { method: 'POST', body: { formula, columnId } });

export const useSaveLayout = () =>
  useWrite((v: { table: 'expenses' | 'daily'; ym: Ym; scope: 'month' | 'future'; columns: LayoutColumn[] }) =>
    api<Layout>(`/api/columns/layout/${v.table}/${v.ym}`, { method: 'PUT', body: { scope: v.scope, columns: v.columns } }),
  );

export const useLayout = (table: 'expenses' | 'daily', ym: Ym) =>
  useQuery({ queryKey: ['layout', table, ym], queryFn: () => api<Layout>(`/api/columns/layout/${table}/${ym}`) });

// ---------- Income ----------

export const useSavePlan = () =>
  useWrite((v: { effectiveFrom: Ym; baseSalary: number; allowances: number; note: string }) =>
    api<SalaryPlan>('/api/income/plans', { method: 'PUT', body: v }),
  );

export const useDeletePlan = () => useWrite((id: number) => api<void>(`/api/income/plans/${id}`, { method: 'DELETE' }));

export const useSaveStatement = () =>
  useWrite(
    (v: { earned: Ym; funded: Ym; note: string; components: SalaryComponent[]; version?: number }) =>
      api<{ id: number }>(`/api/income/statements/${v.earned}`, { method: 'PUT', body: v }),
    { quiet: true },
  );

export const useAddSalaryPayment = () =>
  useWrite((v: { earned: Ym } & ReceiptInput) => api<Receipt>(`/api/income/statements/${v.earned}/payments`, { method: 'POST', body: v }), {
    quiet: true,
  });

export const useSaveReceipt = () =>
  useWrite(
    ({ id, ...v }: ReceiptInput & { id?: number }) =>
      id ? api<Receipt>(`/api/income/receipts/${id}`, { method: 'PUT', body: v }) : api<Receipt>('/api/income/receipts', { method: 'POST', body: v }),
    { quiet: true },
  );

export const useDeleteReceipt = () => useWrite((id: number) => api<void>(`/api/income/receipts/${id}`, { method: 'DELETE' }));

// ---------- Budgets, recurring, month settings ----------

export const useSetBudget = () =>
  useWrite((v: { ym: Ym; categoryId: number; amount: number | null; scope: 'month' | 'future' }) =>
    api<BudgetMonth>(`/api/budgets/${v.ym}/${v.categoryId}`, { method: 'PUT', body: { amount: v.amount, scope: v.scope } }),
  );

export const useSaveRecurring = () =>
  useWrite(
    ({ id, from, ...v }: RecurringInput & { id?: number; from?: Ym }) =>
      id ? api<RecurringItem>(`/api/recurring/${id}?from=${from}`, { method: 'PUT', body: v }) : api<RecurringItem>('/api/recurring', { method: 'POST', body: v }),
    { quiet: true },
  );

export const useStopRecurring = () => useWrite((v: { id: number; from: Ym }) => api<void>(`/api/recurring/${v.id}?from=${v.from}`, { method: 'DELETE' }));

export const useOccurrenceAction = () =>
  useWrite((v: { id: number; action: 'pay' | 'unpay' | 'skip' | 'unskip'; date?: string; amount?: number }) =>
    api<unknown>(`/api/recurring/occurrences/${v.id}/${v.action}`, {
      method: 'POST',
      body: v.action === 'pay' ? { clientRequestId: newRequestId(), date: v.date, amount: v.amount } : {},
    }),
  );

export const useUpdateOccurrence = () =>
  useWrite((v: { id: number; version: number; amount: number; dueDate: string; note: string }) =>
    api<Occurrence>(`/api/recurring/occurrences/${v.id}`, { method: 'PUT', body: v }),
  );

export const useSaveMonthSettings = () =>
  useWrite((v: { ym: Ym; openingBalance: number | null; reservedSavings: number; note: string; version?: number }) =>
    api<MonthSetting>(`/api/months/${v.ym}/settings`, { method: 'PUT', body: v }),
  );

export const useCopyMonth = () =>
  useWrite((v: { ym: Ym; from: Ym; layout: boolean; budgets: boolean; recurring: boolean }) =>
    api<{ layoutsCopied: number; budgetsCopied: number; occurrencesPlanned: number }>(`/api/months/${v.ym}/copy`, { method: 'POST', body: v }),
  );

// ---------- Categories, accounts, profile, import ----------

export const useSaveCategory = () =>
  useWrite(({ id, ...input }: CategoryInput & { id?: number }): Promise<Category | void> =>
    id
      ? api<void>(`/api/categories/${id}`, { method: 'PUT', body: { isArchived: false, ...input } })
      : api<Category>('/api/categories', { method: 'POST', body: input }),
  );

export const useDeleteCategory = () => useWrite((id: number) => api<void>(`/api/categories/${id}`, { method: 'DELETE' }));

export const useReorderCategories = () => useWrite((orderedIds: number[]) => api<void>('/api/categories/order', { method: 'PUT', body: { orderedIds } }));

export const useSaveAccount = () =>
  useWrite((v: { name: string; type: string; currency: string; openingBalance: number }) => api<Account>('/api/accounts', { method: 'POST', body: v }));

export const useUpdateMe = () => useWrite((v: MeUpdate) => api<Me>('/api/me', { method: 'PUT', body: v }));

export const useImportSheet = () =>
  useWrite((file: File) => {
    const form = new FormData();
    form.append('file', file);
    return api<SheetImportResult>('/api/expenses/import/sheet', { method: 'POST', form });
  });
