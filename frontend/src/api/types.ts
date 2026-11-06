// Mirrors backend/DTOs and backend/Ledger. Months are "2026-11", dates "2026-11-05", money is a JSON number
// already rounded by the server (the server's figures are authoritative).

export type Ym = string;
export type IsoDate = string;
export type Timing = 'past' | 'current' | 'future';
export type CategoryType = 'Income' | 'Expense';
export type TxKind = 'Expense' | 'Refund' | 'Transfer';
export type TxStatus = 'Paid' | 'Planned';

export interface AuthResponse {
  token: string;
  userId: number;
  email: string;
}

export interface Me {
  id: number;
  email: string;
  displayName: string;
  currency: string;
  timeZone: string;
  salaryLagMonths: number;
  carryoverEnabled: boolean;
  defaultAccountId: number | null;
  today: IsoDate;
  currentMonth: Ym;
  supportedCurrencies: string[];
  paymentMethods: string[];
  needsSetup: boolean;
}

export interface MeUpdate {
  displayName: string;
  currency: string;
  timeZone: string;
  salaryLagMonths: number;
  carryoverEnabled: boolean;
}

// ---------- Calculator figures ----------

export interface SalaryComponent {
  kind: string;
  label: string;
  amount: number;
}

export interface SalaryFigures {
  statementId: number | null;
  earned: Ym;
  funded: Ym;
  isPlanned: boolean;
  expectedNet: number;
  received: number;
  outstanding: number;
  overpaid: number;
  status: string;
  components: SalaryComponent[];
}

export interface CategoryFigures {
  categoryId: number;
  name: string;
  color: string;
  group: string | null;
  isArchived: boolean;
  gross: number;
  refunds: number;
  net: number;
  budget: number | null;
  budgetRemaining: number | null;
  percentOfBudget: number | null;
  isOverBudget: boolean;
}

export interface Commitment {
  source: 'recurring' | 'planned';
  id: number;
  dueDate: IsoDate;
  amount: number;
  categoryId: number | null;
  name: string;
}

export interface DayFigures {
  date: IsoDate;
  net: number;
  byCategory: Record<string, number>;
}

export interface MonthFigures {
  month: Ym;
  timing: Timing;
  fundingSalaries: SalaryFigures[];
  expectedSalary: number;
  salaryReceivedTowardFunding: number;
  salaryOutstanding: number;
  salaryOverpaid: number;
  salaryIncome: number;
  otherIncome: number;
  // Received other income per category (null = uncategorised), in category order.
  otherIncomeSources: { categoryId: number | null; name: string; color: string; amount: number }[];
  receivedIncome: number;
  expectedOtherIncome: number;
  openingBalance: number;
  openingSource: 'none' | 'manual' | 'carryover';
  availableFunds: number;
  grossSpending: number;
  refunds: number;
  netSpending: number;
  additionalGross: number;
  additionalNet: number;
  plannedSpending: number;
  remainingBalance: number;
  surplus: number;
  deficit: number;
  categories: CategoryFigures[];
  groups: { name: string; net: number; budget: number | null; members: string[] }[];
  totalBudget: number | null;
  overallBudgetRemaining: number | null;
  unbudgetedSpending: number;
  unbudgetedCategories: string[];
  budgetCoverageWarning: boolean;
  incomeUsedPercent: number | null;
  daysInMonth: number;
  elapsedDays: number | null;
  averageDailySpending: number | null;
  daysWithSpending: number;
  remainingDaysIncludingToday: number | null;
  unpaidCommitments: number;
  commitments: Commitment[];
  reservedSavings: number;
  dailyAllowance: number | null;
  forecastIncome: number;
  forecastRemaining: number;
  days: DayFigures[];
  topCategories: CategoryFigures[];
}

export interface CashFlow {
  month: Ym;
  salaryReceived: number;
  otherReceived: number;
  totalReceived: number;
  netSpending: number;
  netCashFlow: number;
}

export interface MonthSummary {
  currency: string;
  today: IsoDate;
  basis: string;
  figures: MonthFigures;
  previous: {
    month: Ym;
    receivedIncome: number;
    netSpending: number;
    remainingBalance: number;
    netSpendingChange: number;
    netSpendingChangePercent: number | null;
    receivedIncomeChange: number;
  };
  cashFlow: CashFlow;
}

export interface AnnualRow {
  month: Ym;
  timing: Timing | '';
  expectedSalary: number;
  salaryIncome: number;
  otherIncome: number;
  receivedIncome: number;
  openingBalance: number;
  grossSpending: number;
  refunds: number;
  netSpending: number;
  additionalNet: number;
  surplus: number;
  remainingBalance: number;
  incomeUsedPercent: number | null;
  totalBudget: number | null;
  unpaidCommitments: number;
}

export interface AnnualSummary {
  year: number;
  currency: string;
  basis: string;
  rows: AnnualRow[];
  totals: AnnualRow;
  categories: { categoryId: number; name: string; color: string; net: number }[];
}

// ---------- Ledger ----------

export interface CellValue {
  value?: unknown;
  display?: string | null;
  error?: string | null;
}

export interface ExpenseRow {
  id: number;
  version: number;
  date: IsoDate;
  amount: number;
  kind: TxKind;
  status: TxStatus;
  categoryId: number | null;
  categoryName: string;
  categoryColor: string;
  description: string;
  paymentMethod: string;
  isAdditional: boolean;
  isRecurring: boolean;
  recurringOccurrenceId: number | null;
  refundOfId: number | null;
  refundedAmount: number;
  accountId: number;
  accountName: string;
  transferAccountId: number | null;
  transferAccountName: string | null;
  custom: Record<string, unknown>;
  computed: Record<string, CellValue>;
}

export interface LedgerTotals {
  count: number;
  gross: number;
  refunds: number;
  net: number;
  planned: number;
  transfers: number;
  additional: number;
}

export type ColumnType = 'text' | 'money' | 'number' | 'date' | 'dropdown' | 'checkbox' | 'formula';

export interface Column {
  key: string;
  id: number | null;
  name: string;
  type: ColumnType;
  builtIn: boolean;
  essential: boolean;
  editable: boolean;
  options: string[];
  formula: string | null;
  formulaError: string | null;
  decimals: number;
  isArchived: boolean;
  version: number;
}

export interface LayoutColumn {
  key: string;
  width?: number | null;
  hidden: boolean;
}

export interface Layout {
  table: 'expenses' | 'daily';
  source: 'month' | 'template' | 'default';
  sourceMonth: Ym | null;
  columns: LayoutColumn[];
}

export interface ExpenseList {
  month: Ym;
  currency: string;
  columns: Column[];
  layout: Layout;
  rows: ExpenseRow[];
  filtered: LedgerTotals;
  monthTotals: LedgerTotals;
  isFiltered: boolean;
}

export interface ExpenseFilter {
  search?: string;
  categoryId?: number;
  from?: IsoDate;
  to?: IsoDate;
  status?: TxStatus | '';
  kind?: TxKind | '';
  additional?: boolean;
  paymentMethod?: string;
  sort?: string;
  dir?: 'asc' | 'desc';
}

export interface ExpenseInput {
  clientRequestId?: string;
  date: IsoDate;
  amount: number;
  kind: TxKind;
  status: TxStatus;
  categoryId?: number | null;
  description: string;
  paymentMethod: string;
  isAdditional: boolean;
  accountId?: number | null;
  transferAccountId?: number | null;
  refundOfId?: number | null;
  custom?: Record<string, unknown>;
  version?: number;
}

export interface ExpensePatch {
  version: number;
  date?: IsoDate;
  amount?: number;
  status?: TxStatus;
  categoryId?: number;
  description?: string;
  paymentMethod?: string;
  isAdditional?: boolean;
  custom?: Record<string, unknown>;
}

export interface MatrixEntry {
  id: number;
  version: number;
  amount: number;
  description: string;
  status: TxStatus;
  kind: TxKind;
  paymentMethod: string;
  isAdditional: boolean;
}

export interface MatrixCell {
  net: number;
  planned: number;
  entries: MatrixEntry[];
}

export interface MatrixDay {
  date: IsoDate;
  cells: Record<string, MatrixCell>;
  net: number;
  note: string;
}

export interface MatrixCategory {
  id: number;
  name: string;
  color: string;
  groupName: string | null;
  isArchived: boolean;
  hidden: boolean;
  width: number | null;
}

export interface Matrix {
  month: Ym;
  currency: string;
  today: IsoDate;
  layoutSource: string;
  categories: MatrixCategory[];
  days: MatrixDay[];
  columnTotals: Record<string, number>;
  net: number;
}

// ---------- Income ----------

export interface Allocation {
  month: Ym;
  amount: number;
}

export interface Receipt {
  id: number;
  version: number;
  kind: 'Salary' | 'Other';
  salaryStatementId: number | null;
  salaryEarned: Ym | null;
  categoryId: number | null;
  categoryName: string;
  status: 'Received' | 'Expected';
  receivedDate: IsoDate | null;
  expectedDate: IsoDate | null;
  amount: number;
  unallocated: number;
  description: string;
  paymentMethod: string;
  allocations: Allocation[];
}

export interface ReceiptInput {
  clientRequestId?: string;
  kind: 'Salary' | 'Other';
  categoryId?: number | null;
  status: 'Received' | 'Expected';
  receivedDate?: IsoDate | null;
  expectedDate?: IsoDate | null;
  amount: number;
  description: string;
  paymentMethod: string;
  allocations?: Allocation[] | null;
  defaultMonth?: Ym | null;
  version?: number;
}

export interface SalaryStatement {
  id: number | null;
  version: number;
  earned: Ym;
  funded: Ym;
  isPlanned: boolean;
  note: string;
  components: SalaryComponent[];
  expectedNet: number;
  received: number;
  outstanding: number;
  overpaid: number;
  status: string;
  payments: Receipt[];
}

export interface IncomeMonth {
  month: Ym;
  currency: string;
  salaryLagMonths: number;
  funding: SalaryStatement[];
  earnedThisMonth: SalaryStatement | null;
  otherIncome: Receipt[];
  expectedSalary: number;
  salaryReceived: number;
  salaryDue: number;
  salaryOverpaid: number;
  salaryIncome: number;
  otherIncomeReceived: number;
  expectedOtherIncome: number;
  totalReceivedIncome: number;
}

export interface SalaryPlan {
  id: number;
  effectiveFrom: Ym;
  baseSalary: number;
  allowances: number;
  note: string;
  version: number;
}

// ---------- Budgets, recurring, settings ----------

export interface BudgetLine {
  categoryId: number;
  name: string;
  color: string;
  isArchived: boolean;
  amount: number | null;
  source: 'month' | 'template' | 'none';
  net: number;
  remaining: number | null;
}

export interface BudgetMonth {
  month: Ym;
  lines: BudgetLine[];
  totalBudget: number | null;
  overallRemaining: number | null;
  unbudgetedSpending: number;
  unbudgetedCategories: string[];
  coverageWarning: boolean;
}

export interface RecurringItem {
  id: number;
  seriesId: string;
  kind: 'Expense' | 'Income';
  description: string;
  categoryId: number | null;
  categoryName: string;
  amount: number;
  dayOfMonth: number;
  paymentMethod: string;
  start: Ym;
  end: Ym | null;
  isActive: boolean;
  version: number;
}

export interface RecurringInput {
  kind: 'Expense' | 'Income';
  description: string;
  categoryId: number | null;
  amount: number;
  dayOfMonth: number;
  paymentMethod: string;
  start: Ym;
  end: Ym | null;
  version?: number;
}

export interface Occurrence {
  id: number;
  version: number;
  recurringItemId: number;
  seriesId: string;
  kind: 'Expense' | 'Income';
  description: string;
  categoryId: number | null;
  categoryName: string;
  dueDate: IsoDate;
  amount: number;
  status: 'Planned' | 'Paid' | 'Skipped';
  isOverridden: boolean;
  transactionId: number | null;
  incomeReceiptId: number | null;
  note: string;
}

export interface RecurringMonth {
  month: Ym;
  items: RecurringItem[];
  occurrences: Occurrence[];
  plannedUnpaid: number;
}

export interface MonthSetting {
  month: Ym;
  openingBalance: number | null;
  reservedSavings: number;
  note: string;
  carryoverEnabled: boolean;
  version: number;
}

export interface Category {
  id: number;
  name: string;
  type: CategoryType;
  color: string;
  sortOrder: number;
  groupName: string | null;
  isArchived: boolean;
  transactionCount: number;
  version: number;
}

export interface CategoryInput {
  name: string;
  type: CategoryType;
  color?: string | null;
  groupName?: string | null;
  isArchived?: boolean;
  // The version the edit started from; a stale one gets 409.
  version?: number;
}

export interface Account {
  id: number;
  name: string;
  type: string;
  currency: string;
  openingBalance: number;
}

export interface SheetImportResult {
  rowsRead: number;
  daysImported: number;
  entriesCreated: number;
  notesImported: number;
  salariesImported: number;
  cellsSkipped: number;
  categoriesCreated: string[];
  warnings: string[];
}

export interface LedgerChange {
  scope: string;
  year: number | null;
  month: number | null;
  origin: string | null;
}
