import { EditedPath, PathsDiff } from 'json-difference';

export interface IJsonDiffService {
  getJsonDiff(oldJsonData: string, newJsonData: string): JsonDiffOutput;
}

export interface JsonDiffOutput {
  addedItems: ICustomPathsDiff[];
  updatedItems: EditedPath[];
  deletedItems: ICustomPathsDiff[];
}

// export interface ICustomChange extends IChange {}

export interface ICustomPathsDiff extends PathsDiff {}
export interface ICustomEditedPathsDiff extends EditedPath {}
