#!/usr/bin/env python3
"""Regression checks for the public fork's GitHub Actions build gate."""

from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github" / "workflows"
PINNED_WORKFLOWS = (
    "main.yml",
    "deploy_docs.yml",
    "docker-smoke.yml",
    "mirror-smoke.yml",
    "postgresql-smoke.yml",
    "release-helm.yml",
)
PINNED_ACTIONS = {"actions/checkout", "actions/setup-dotnet", "actions/setup-node"}


class WorkflowSafetyTests(unittest.TestCase):
    def test_workflow_only_pull_requests_are_not_ignored(self):
        main = (WORKFLOWS / "main.yml").read_text(encoding="utf-8")
        self.assertRegex(main, r"(?m)^  pull_request:\s*$")
        self.assertNotIn(".github/**", main)

    def test_build_job_token_is_read_only(self):
        main = (WORKFLOWS / "main.yml").read_text(encoding="utf-8")
        self.assertRegex(main, r"(?m)^permissions:\s*\n  contents: read\s*$")

    def test_common_actions_are_sha_pinned(self):
        inspected = 0
        for name in PINNED_WORKFLOWS:
            path = WORKFLOWS / name
            with self.subTest(workflow=name):
                content = path.read_text(encoding="utf-8")
                for line_no, line in enumerate(content.splitlines(), start=1):
                    match = re.match(
                        r"\s*-?\s*uses:\s*([\w./-]+)@([^\s#]+)", line
                    )
                    if match and match.group(1) in PINNED_ACTIONS:
                        inspected += 1
                        with self.subTest(workflow=name, line=line_no):
                            self.assertRegex(match.group(2), r"\A[0-9a-f]{40}\Z")
        self.assertGreater(inspected, 0, "No pinned actions were inspected")


if __name__ == "__main__":
    unittest.main()
