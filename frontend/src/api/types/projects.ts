export interface ProjectRecord {
  projectId: string;
  projectCode: string;
  projectName: string;
  description: string | null;
  projectStatus: string;
  createdAt: string;
  updatedAt: string;
  rowVersion: number;
}

export interface ChapterRecord {
  id: string;
  projectId: string;
  parentId: string | null;
  chapterCode: string;
  title: string;
  levelNo: number;
  sortKey: number;
  workflowStatus: string;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
  rowVersion: number;
}

