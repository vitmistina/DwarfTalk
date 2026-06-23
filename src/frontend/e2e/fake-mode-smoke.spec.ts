import { expect, test } from "@playwright/test";

test("loads list, selects dwarf, chats, and shows safe fake diagnostics", async ({ page }) => {
  await page.goto("/");

  const dwarfListPanel = page.locator("article", {
    has: page.getByRole("heading", { name: "Dwarf list" }),
  });
  await expect(dwarfListPanel).toBeVisible();

  const selectedDwarfButton = page.getByRole("button", { name: /Iden Torrentshade/i });
  await selectedDwarfButton.click();
  await expect(selectedDwarfButton).toHaveAttribute("aria-pressed", "true");

  const selectedDwarfPanel = page.locator("article", {
    has: page.getByRole("heading", { name: "Selected dwarf" }),
  });
  await expect(selectedDwarfPanel.locator(".dwarf-selected-name strong")).toHaveText("Iden Torrentshade");
  await expect(selectedDwarfPanel.getByText("Current job")).toBeVisible();
  await expect(selectedDwarfPanel.locator("dd").first()).not.toHaveText("No current job");

  const chatPanel = page.locator("article", {
    has: page.getByRole("heading", { name: "Chat" }),
  });
  await expect(chatPanel.getByText("Chat target:")).toContainText("Iden Torrentshade");

  const message = "What do you see around you right now?";
  await chatPanel.getByLabel("Message").fill(message);
  await chatPanel.getByRole("button", { name: "Send" }).click();

  const conversation = page.getByRole("list", { name: "Conversation" });
  await expect(conversation.getByText(message)).toBeVisible();
  await expect(conversation.getByText(/I can see/i)).toBeVisible();
  await expect(conversation.getByText("Perception")).toBeVisible();
  await expect(conversation.getByText("look_around")).toBeVisible();
  await expect(conversation.getByText("Success")).toBeVisible();

  const diagnostics = conversation.getByText(
    /Provider:\s*Fake.*Model:\s*fake-dwarf.*Duration:\s*\d+ms.*Prompt:\s*prompt-[0-9a-f]{12}/,
  );
  await expect(diagnostics).toBeVisible();
  await expect(diagnostics).not.toContainText(message);
  await expect(diagnostics).not.toContainText("Authorization");
  await expect(conversation).not.toContainText("\"cells\"");
  await expect(conversation).not.toContainText("radius");
});
