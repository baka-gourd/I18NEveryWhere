# I18n Everywhere

This mod offers various methods to simplify the localization of game mods, including:

- Embedded locale files
- Centralized locale files
- Language packs

This primarily addresses issues where some mods lack a way to submit localization files, do not implement localization, or experience localization failures or unpublished updates due to infrequent updates.

Of course, using this mod makes implementing localization much simpler.

## Features

### Embedded Locale Files

Streamline localization by reducing the workload for mod authors while ensuring centralized support.

This method eliminates the need for authors to:

- Implement custom language file loading
- Handle different text encodings manually

#### How to Use

1. Create a folder named **"lang"** in your mod directory.
2. Add localization files in JSON format, such as `en-US.json` or `zh-HANS.json`.

---

### Centralized Locale Files

Contribute your localization files to the community through platforms like [GitHub][github], [Discord][discord], or ParatransZ.

Updates are typically rolled out on a **weekly** or **bi-weekly** basis and included in new mod versions.

---

### Language Packs

Create and manage custom language packs to maintain full control over your localization.

#### Steps to Create a Language Pack

1. Add an `i18n.json` file to the root directory of your mod.
2. Organize your files in a structure similar to the [Localization directory on GitHub][github].
3. Add **I18NE** as a dependency in your mod.

#### Example

[European Portuguese Localization][pt] by Ti4goc

[github]: https://github.com/baka-gourd/I18NEverywhere.Localization
[discord]: https://discord.com/channels/1024242828114673724/1224162446537654393
[pt]: https://mods.paradoxplaza.com/mods/92599/Windows