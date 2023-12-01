import { isNil } from 'lodash-es';

export class OcadDirectoryHelper {
  public static isValidReleaseFolderName(name?: string): boolean {
    if (isNil(name) || name.length === 0) return false;
    if (name.length >= 2 && name.startsWith('V', 0) && !isNaN(+name[1])) {
      return true;
    }
    return false;
  }
}
