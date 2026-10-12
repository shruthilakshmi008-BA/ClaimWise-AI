// Mirrors the API's DTOs (see docs/api.md). Enums are strings on the wire.

export const CLAIM_TYPES = ['Outpatient', 'Inpatient', 'Pharmacy', 'Dental'] as const;
export type ClaimType = (typeof CLAIM_TYPES)[number];

export const CLAIM_STATUSES = ['Submitted', 'InReview', 'NeedsInformation', 'Approved', 'Rejected'] as const;
export type ClaimStatus = (typeof CLAIM_STATUSES)[number];

export const DOCUMENT_TYPES = ['ClaimForm', 'Invoice', 'MedicalReport', 'Prescription', 'DischargeSummary'] as const;
export type DocumentType = (typeof DOCUMENT_TYPES)[number];

export interface ClaimSummary {
  id: number;
  claimNumber: string;
  patientName: string;
  providerName: string;
  claimType: ClaimType;
  amountClaimed: number;
  serviceDate: string;
  status: ClaimStatus;
  submittedAt: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ClaimDocument {
  id: number;
  documentType: DocumentType;
  fileName: string;
  receivedAt: string;
  sizeBytes: number | null;
  sha256: string | null;
  hasFile: boolean;
}

export interface Validation {
  isComplete: boolean;
  requiredDocuments: DocumentType[];
  missingDocuments: DocumentType[];
  missingFields: string[];
}

export interface ClaimDetail {
  id: number;
  claimNumber: string;
  patientName: string;
  memberId: string;
  policyNumber: string;
  providerName: string;
  claimType: ClaimType;
  diagnosisCode: string | null;
  procedureCode: string | null;
  amountClaimed: number;
  serviceDate: string;
  status: ClaimStatus;
  submittedAt: string;
  updatedAt: string;
  version: number;
  reviewerName: string | null;
  reviewerNote: string | null;
  documents: ClaimDocument[];
  validation: Validation;
  allowedNextStatuses: ClaimStatus[];
}

export interface Dashboard {
  totalClaims: number;
  byStatus: Record<ClaimStatus, number>;
  totalAmountClaimed: number;
  openClaimsWithIncompleteSubmission: number;
}

export interface AuditEntry {
  id: number;
  action: string;
  details: string;
  actor: string;
  timestamp: string;
}

export interface CreateClaimInput {
  patientName: string;
  memberId: string;
  policyNumber: string;
  providerName: string;
  claimType: ClaimType;
  diagnosisCode?: string;
  procedureCode?: string;
  amountClaimed: number;
  serviceDate: string;
}

export interface ClaimListParams {
  status?: ClaimStatus | '';
  claimType?: ClaimType | '';
  search?: string;
  sortBy?: 'submittedAt' | 'serviceDate' | 'amount' | 'claimNumber' | 'status';
  sortDir?: 'asc' | 'desc';
  page?: number;
  pageSize?: number;
}

// ---------- policies ----------

export interface PolicyDocument {
  id: number;
  title: string;
  policyFamily: string | null;
  version: string | null;
  effectiveDate: string | null;
  fileName: string;
  sizeBytes: number;
  pageCount: number;
  chunkCount: number;
  isActive: boolean;
  uploadedAt: string;
  uploadedBy: string;
  sha256: string;
}

export interface PolicyChunk {
  id: number;
  ordinal: number;
  clauseRef: string | null;
  heading: string | null;
  sectionPath: string;
  pageNumber: number;
  startOffset: number;
  endOffset: number;
  wordCount: number;
  text: string;
}

export interface Passage {
  rank: number;
  chunkId: number;
  documentId: number;
  documentTitle: string;
  documentVersion: string | null;
  clauseRef: string | null;
  heading: string | null;
  sectionPath: string;
  pageNumber: number;
  text: string;
  rerankScore: number;
  fusedScore: number;
  keywordScore: number | null;
  keywordRank: number | null;
  vectorScore: number | null;
  vectorRank: number | null;
}

export interface PolicySearchResponse {
  query: string;
  mode: 'Hybrid' | 'KeywordOnly' | 'VectorOnly';
  keywordCandidates: number;
  vectorCandidates: number;
  passages: Passage[];
}

export interface AiStatus {
  embeddingProvider: string;
  embeddingModel: string;
  summaryGenerator: string;
  summaryModel: string | null;
  activeDocuments: number;
  indexedChunks: number;
  staleChunks: number;
  summariesUseLanguageModel: boolean;
}

// ---------- AI summaries ----------

export type AiSummaryStatus = 'Answered' | 'Abstained' | 'Failed';
export type SummaryReviewState = 'NotRequired' | 'PendingReview' | 'Accepted' | 'Rejected';

export interface Citation {
  passage: string;
  quote: string;
}

export interface SummaryStatement {
  ordinal: number;
  text: string;
  origin: 'Model' | 'System';
  citations: Citation[];
}

export interface SummaryPassage {
  label: string;
  rank: number;
  chunkId: number;
  documentId: number;
  documentTitle: string;
  documentVersion: string | null;
  clauseRef: string | null;
  heading: string | null;
  sectionPath: string;
  pageNumber: number;
  text: string;
  rerankScore: number;
  keywordScore: number | null;
  vectorScore: number | null;
}

export interface AiSummary {
  id: number;
  claimId: number;
  question: string;
  status: AiSummaryStatus;
  generatedAt: string;
  requestedBy: string;
  generator: string;
  model: string | null;
  embeddingModel: string;
  usedLanguageModel: boolean;
  claimVersionAtGeneration: number;
  statements: SummaryStatement[];
  passages: SummaryPassage[];
  evidence: {
    verdict: 'Sufficient' | 'Insufficient';
    reasons: string[];
    topScore: number;
    coverage: number;
    supportingPassages: number;
    missingTerms: string[];
  };
  conflicts: { kind: string; passages: string[]; description: string }[];
  warnings: string[];
  rejectedStatements: { text: string; reason: string }[];
  abstainReason: string | null;
  failureReason: string | null;
  review: { state: SummaryReviewState; reviewedBy: string | null; reviewedAt: string | null; note: string | null };
  notice: string;
}
