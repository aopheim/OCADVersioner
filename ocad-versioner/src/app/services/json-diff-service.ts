import { Injectable } from '@angular/core';
import { OcadVersionerModule } from '../components/ocad-file-uploader/ocad-versioner.module';
import { IJsonDiffService, JsonDiffOutput } from './json-diff-service.models';
import { getDiff } from 'json-difference';

@Injectable({ providedIn: OcadVersionerModule })
export class JsonDiffService implements IJsonDiffService {
  getJsonDiff(oldJsonData: any, newJsonData: any): JsonDiffOutput {
    // const diffs = diff(oldJsonData, newJsonData);
    const diffs = getDiff(
      JSON.stringify(oldJsonData),
      JSON.stringify(newJsonData)
    );

    return {
      addedItems: diffs.added,
      deletedItems: diffs.removed,
      updatedItems: diffs.edited,
    } as JsonDiffOutput;
    // return {
    //   addedItems: diffs.filter((d) => d.type === Operation.ADD),
    //   updatedItems: diffs.filter((d) => d.type === Operation.UPDATE),
    //   deletedItems: diffs.filter((d) => d.type === Operation.REMOVE),
    // } as JsonDiffOutput;
  }
}
