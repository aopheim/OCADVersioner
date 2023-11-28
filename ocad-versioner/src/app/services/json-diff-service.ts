import { Injectable } from '@angular/core';
import { IJsonDiffService } from './json-diff-service.models';
import { getDiff } from 'json-difference';
import { OcadDiffDto } from '../components/ocad-diff-table/ocad-diff-table.models';
import { diff } from 'json-diff-ts';

@Injectable()
export class JsonDiffService implements IJsonDiffService {
  getJsonDiff(oldJsonData: any, newJsonData: any): OcadDiffDto {
    console.log('newData', newJsonData);
    const diffs = diff(
      JSON.stringify(oldJsonData),
      JSON.stringify(newJsonData),
      {}
    );
    console.log('diffs: ', diffs);

    return {
      added: [],
      deleted: [],
      edited: [],
    } as OcadDiffDto;
  }
}
